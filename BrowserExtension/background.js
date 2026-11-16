import {ProfileAgent} from "./agent.mjs";
const agent=new ProfileAgent(chrome);
const ready=agent.init().then(()=>agent.sync());
chrome.windows.onFocusChanged.addListener(id=>{if(id>0)ready.then(()=>agent.sync(id)).catch(()=>{});else ready.then(()=>agent.sync()).catch(()=>{});});
chrome.windows.onBoundsChanged.addListener(()=>ready.then(()=>agent.sync()).catch(()=>{}));
chrome.windows.onCreated.addListener(()=>ready.then(()=>agent.sync()).catch(()=>{}));
chrome.windows.onRemoved.addListener(()=>ready.then(()=>agent.sync()).catch(()=>{}));
chrome.alarms.create("reconnect",{periodInMinutes:0.5});
chrome.alarms.onAlarm.addListener(()=>ready.then(()=>agent.sync()).catch(()=>{}));
setInterval(()=>ready.then(()=>agent.sync()).catch(()=>{}),1000);
chrome.runtime.onMessage.addListener((request,_sender,respond)=>{
  agent.retryAfter=0;
  ready.then(async()=>{if(request.type==="configure")return agent.configure(request.name,request.browser);await agent.sync();return agent.snapshot();}).then(respond).catch(error=>respond({available:false,message:String(error)}));return true;
});
