export const HOST = "com.freeapplock.profiles";
export class ProfileAgent {
  constructor(api) {
    this.api=api; this.port=null; this.retryAfter=0; this.syncQueued=false; this.pendingUnlock=null; this.pending=new Map(); this.sequence=0; this.approved=new Set();
    this.owned=new Map(); this.protected=false; this.available=false; this.message="Connecting…"; this.chain=Promise.resolve();
  }
  async init() {
    const data=await this.api.storage.local.get(["profileId","name","browser"]);
    this.profileId=data.profileId || crypto.randomUUID(); this.name=data.name || "My profile"; this.browser=data.browser || "brave";
    await this.api.storage.local.set({profileId:this.profileId,name:this.name,browser:this.browser});
    const state=await this.api.storage.session.get("owned"); this.owned=new Map(state.owned || []);
    this.connect();
  }
  connect() {
    if(this.port||Date.now()<this.retryAfter)return;
    try {
      this.port=this.api.runtime.connectNative(HOST);
      this.port.onMessage.addListener(reply=>{const item=this.pending.get(reply.requestId);if(item){clearTimeout(item.timer);this.pending.delete(reply.requestId);item.resolve(reply);}});
      this.port.onDisconnect.addListener(()=>{
        this.retryAfter=Date.now()+30000; const error=this.api.runtime.lastError; this.message=error?.message || "Desktop connection closed"; this.port=null;
        for(const item of this.pending.values()){clearTimeout(item.timer);item.resolve({available:false,protected:false,message:this.message});} this.pending.clear();
        this.available=false;this.protected=false;this.release().catch(()=>{});this.status();
      });
    } catch(error) {this.port=null;this.message=String(error);}
  }
  send(packet) {
    this.connect();if(!this.port)return Promise.resolve({available:false,protected:false,message:"Desktop host is unavailable"});
    packet.requestId=String(++this.sequence);
    return new Promise(resolve=>{
      const timer=setTimeout(()=>{this.pending.delete(packet.requestId);resolve({available:false,protected:false,message:"Desktop did not respond"});},2500);
      this.pending.set(packet.requestId,{resolve,timer});
      try{this.port.postMessage(packet);}catch{clearTimeout(timer);this.pending.delete(packet.requestId);resolve({available:false,protected:false,message:"Desktop connection failed"});}
    });
  }
  sync(unlockWindow=null) {
    if(unlockWindow)this.pendingUnlock=unlockWindow;
    if(this.syncQueued)return this.chain;
    this.syncQueued=true;
    this.chain=this.chain.catch(()=>{}).then(()=>{this.syncQueued=false;const target=this.pendingUnlock;this.pendingUnlock=null;return this.perform(target);});
    return this.chain;
  }
  async remember() {await this.api.storage.session.set({owned:[...this.owned]});}
  async hide(window) {
    if(window.state==="minimized")return;
    if(!this.owned.has(window.id))this.owned.set(window.id,["normal","maximized","fullscreen"].includes(window.state)?window.state:"normal");
    await this.remember();await this.api.windows.update(window.id,{state:"minimized"});
  }
  async restore(id,focus) {
    if(!this.owned.has(id))return;
    const state=this.owned.get(id);
    try {const window=await this.api.windows.get(id);if(window.state==="minimized")await this.api.windows.update(id,{state,focused:focus});}catch{}
    this.owned.delete(id);await this.remember();
  }
  async release() {for(const id of [...this.owned.keys()])await this.restore(id,false);}
  async perform(unlockWindow) {
    let windows=await this.api.windows.getAll({populate:false,windowTypes:["normal","popup"]});
    windows=windows.filter(w=>!w.incognito);
    const live=new Set(windows.map(w=>w.id));for(const id of this.owned.keys())if(!live.has(id))this.owned.delete(id);
    if(this.protected && unlockWindow && !this.approved.has(unlockWindow)) {
      const target=windows.find(w=>w.id===unlockWindow);if(target){await this.hide(target);windows=await this.api.windows.getAll({populate:false,windowTypes:["normal","popup"]});windows=windows.filter(w=>!w.incognito);}
    }
    const reply=await this.send({type:unlockWindow?"unlock":"sync",profileId:this.profileId,name:this.name,browser:this.browser,
      windows:windows.map(w=>({id:w.id,focused:w.focused,minimized:w.state==="minimized"})),unlockWindow:live.has(unlockWindow)?unlockWindow:null});
    this.available=reply.available===true;this.protected=this.available&&reply.protected===true;this.message=reply.message||"Connected";
    this.approved=new Set(reply.approved||[]);
    if(!this.protected)await this.release();
    else for(const command of reply.commands||[]) {
      const window=windows.find(w=>w.id===command.id);if(!window)continue;
      if(command.action==="lock") {
        await this.hide(window);
        if(window.focused && window.state!=="minimized" && !unlockWindow)queueMicrotask(()=>this.sync(window.id));
      } else if(command.action==="restore")await this.restore(command.id,true);
    }
    this.status();return this.snapshot();
  }
  snapshot(){return {profileId:this.profileId,name:this.name,browser:this.browser,available:this.available,protected:this.protected,message:this.message};}
  status(){this.api.action.setBadgeText({text:!this.available?"!":this.protected?"ON":""}).catch(()=>{});this.api.action.setBadgeBackgroundColor({color:this.available?"#147d91":"#a7651f"}).catch(()=>{});}
  async configure(name,browser) {
    this.name=String(name).trim().slice(0,80)||"My profile";this.browser=["brave","chrome","msedge"].includes(browser)?browser:"brave";
    await this.api.storage.local.set({name:this.name,browser:this.browser});return this.sync();
  }
}
