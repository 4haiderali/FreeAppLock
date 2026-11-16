import test from "node:test";
import assert from "node:assert/strict";
import {ProfileAgent} from "../BrowserExtension/agent.mjs";
function event(){const callbacks=[];return {addListener:f=>callbacks.push(f),emit:x=>callbacks.forEach(f=>f(x))};}
function fixture(profileId,windows,handler){
  let local={profileId,name:"Work",browser:"brave"},session={};const updates=[],packets=[];
  const port={onMessage:event(),onDisconnect:event(),postMessage(packet){packets.push(structuredClone(packet));queueMicrotask(()=>port.onMessage.emit({...handler(packet),requestId:packet.requestId}));}};
  const api={runtime:{connectNative:()=>port},storage:{local:{get:async()=>structuredClone(local),set:async value=>Object.assign(local,value)},session:{get:async()=>structuredClone(session),set:async value=>Object.assign(session,value)}},
    action:{setBadgeText:async()=>{},setBadgeBackgroundColor:async()=>{}},windows:{
      getAll:async options=>{assert.equal(options.populate,false);return structuredClone(windows);},
      get:async id=>structuredClone(windows.find(w=>w.id===id)),
      update:async(id,changes)=>{const window=windows.find(w=>w.id===id);assert.ok(window);updates.push({id,...changes});Object.assign(window,changes);if(changes.state==="minimized")window.focused=false;return structuredClone(window);}
    }};
  return {agent:new ProfileAgent(api),windows,updates,packets};
}
test("Selected profile changes only its own windows and never accesses tabs",async()=>{
  let granted=false;
  const work=fixture("work",[{id:1,state:"maximized",focused:false,tabs:["a","b"]},{id:2,state:"normal",focused:false,tabs:["c"]},{id:3,state:"minimized",focused:false,tabs:["d"]},{id:99,state:"normal",focused:false,incognito:true}],packet=>{
    if(packet.type==="unlock")granted=true;
    return {available:true,protected:true,approved:granted?[1]:[],commands:packet.windows.map(w=>({id:w.id,action:granted&&w.id===1?"restore":"lock"}))};
  });
  const personal=fixture("personal",[{id:1,state:"normal",focused:false,tabs:["private-profile"]}],()=>({available:true,protected:false,commands:[]}));
  await work.agent.init();await personal.agent.init();await work.agent.sync();await personal.agent.sync();
  assert.equal(work.windows[0].state,"minimized");assert.equal(personal.windows[0].state,"normal");assert.equal(work.windows[3].state,"normal");
  work.windows[0].state="normal";work.windows[0].focused=true;await work.agent.sync(1);
  assert.equal(work.windows[0].state,"maximized");assert.equal(work.windows[1].state,"minimized");
  assert.deepEqual(work.windows[0].tabs,["a","b"]);assert.deepEqual(work.windows[1].tabs,["c"]);
  assert.ok(work.packets.every(p=>p.profileId==="work"&&!JSON.stringify(p).includes('"tabs"')));
});
test("Unavailable desktop reports loss of protection and restores only companion-minimized windows",async()=>{
  let available=true;const fixtureData=fixture("work",[{id:1,state:"normal",focused:false},{id:2,state:"minimized",focused:false}],packet=>({available,protected:available,commands:available?packet.windows.map(w=>({id:w.id,action:"lock"})):[]}));
  await fixtureData.agent.init();await fixtureData.agent.sync();available=false;await fixtureData.agent.sync();
  assert.equal(fixtureData.agent.protected,false);assert.equal(fixtureData.windows[0].state,"normal");assert.equal(fixtureData.windows[1].state,"minimized");
});

test("Window event bursts coalesce instead of building an unbounded request queue",async()=>{
  const data=fixture("work",[{id:1,state:"normal",focused:false}],()=>({available:true,protected:false,commands:[]}));
  await data.agent.init();await Promise.all(Array.from({length:100},()=>data.agent.sync()));assert.equal(data.packets.length,1);
});
