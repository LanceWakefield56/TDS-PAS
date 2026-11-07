const crypto = require('crypto');
function b32(s){const a='ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';s=s.replace(/[=\s]/g,'').toUpperCase();let bits='';for(const c of s)bits+=a.indexOf(c).toString(2).padStart(5,'0');const out=[];for(let i=0;i+8<=bits.length;i+=8)out.push(parseInt(bits.slice(i,i+8),2));return Buffer.from(out);}
function totp(key,offset=0){const t=Math.floor(Date.now()/30000)+offset;const b=Buffer.alloc(8);b.writeBigUInt64BE(BigInt(t));const h=crypto.createHmac('sha1',b32(key)).update(b).digest();const o=h[h.length-1]&15;return String(((h.readUInt32BE(o)&0x7fffffff)%1000000)).padStart(6,'0');}
const BASE='https://localhost:5001';
const H={'X-BrightPAS-Request':'1'};
async function post(req,path,data){const r=await req.post(BASE+path,{data,headers:H});let j=null;try{j=await r.json();}catch{}return {status:r.status(),json:j};}
const fs=require('fs');
const STATE='/home/claude/devtools/e2e/state.json';
// Sign in as admin (creating first admin if needed); returns a browser context that is signed in.
async function adminContext(browser, bootstrap){
  const ctx=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1366,height:900}});
  const req=ctx.request; const pw='correct horse battery staple';
  let st=fs.existsSync(STATE)?JSON.parse(fs.readFileSync(STATE)):{};
  const s=await (await req.get(BASE+'/api/setup/status')).json();
  if(s.firstAdminNeeded){
    const a=await post(req,'/api/setup/first-admin/start',{code:bootstrap,userName:'admin',displayName:'Lance Admin',password:pw});
    st.key=a.json.sharedKey; const c=await post(req,'/api/setup/first-admin/complete',{code:bootstrap,userName:'admin',totpCode:totp(st.key)});
    if(c.status!==200) throw new Error('first admin '+JSON.stringify(c)); fs.writeFileSync(STATE,JSON.stringify(st));
    await new Promise(r=>setTimeout(r,31000));
  }
  const l=await post(req,'/api/auth/login',{userName:'admin',password:pw});
  let m=await post(req,'/api/auth/mfa',{code:totp(st.key)});
  if(m.status!==200){await new Promise(r=>setTimeout(r,31000)); m=await post(req,'/api/auth/mfa',{code:totp(st.key)});}
  if(m.status!==200) throw new Error('mfa '+JSON.stringify(l)+JSON.stringify(m));
  return ctx;
}
module.exports={totp,post,BASE,H,adminContext};
