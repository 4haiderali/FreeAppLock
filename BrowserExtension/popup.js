const name=document.querySelector("#name"),browser=document.querySelector("#browser"),status=document.querySelector("#status");
function show(value){status.textContent=value.available?(value.protected?"Connected · protection enabled":value.message||"Connected · not protected"):value.message||"Open FreeAppLock to connect";}
chrome.runtime.sendMessage({type:"status"}).then(value=>{name.value=value.name||"";browser.value=value.browser||"brave";show(value);}).catch(error=>{status.textContent=String(error);});
document.querySelector("#save").addEventListener("click",()=>chrome.runtime.sendMessage({type:"configure",name:name.value,browser:browser.value}).then(show).catch(error=>{status.textContent=String(error);}));
