const {chromium}=require('playwright');const {BASE,post,totp}=require('./lib');
// re-uses admin; view pages as admin cannot see roster, so sign in a fresh roster manager
(async()=>{const b=await chromium.launch();const st=JSON.parse(require('fs').readFileSync('state.json'));const adm=await b.newContext({ignoreHTTPSErrors:true});
await post(adm.request,'/api/auth/login',{userName:'admin',password:'correct horse battery staple'});let m=await post(adm.request,'/api/auth/mfa',{code:totp(st.key)});
if(m.status!==200){await new Promise(r=>setTimeout(r,31000));m=await post(adm.request,'/api/auth/mfa',{code:totp(st.key)});}
const pw='correct horse battery staple';const u='snap'+(Date.now()%100000);await post(adm.request,'/api/admin/users',{userName:u,displayName:'Snap',roles:['Roster Manager','Reception / Booking'],password:pw});
const c=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1500,height:1000}});await post(c.request,'/api/auth/login',{userName:u,password:pw});
const e=await post(c.request,'/api/auth/enrol/begin',{});await post(c.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});
const p=await c.newPage();for(const [u,n] of [[process.argv[2],'r1'],[process.argv[3],'r2']]){await p.goto(BASE+u);await p.waitForSelector('.cal');await p.waitForTimeout(600);await p.screenshot({path:`/tmp/snap-${n}.png`,fullPage:true});}
await b.close();})();
