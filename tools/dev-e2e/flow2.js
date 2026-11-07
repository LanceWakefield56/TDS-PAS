const {chromium}=require('playwright');const {BASE,post,totp}=require('./lib');
const shot=async(p,n)=>{await p.waitForTimeout(400);await p.screenshot({path:`/tmp/e2e-${n}.png`,fullPage:true});console.log('shot',n);};
(async()=>{const b=await chromium.launch();const errs=[];
const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1366,height:900}});
// reception has MFA enrolled already: need its key -> re-enrol not possible; so create a new booking user
const fs=require('fs');const st=JSON.parse(fs.readFileSync('state.json'));
const adm=await b.newContext({ignoreHTTPSErrors:true});
await post(adm.request,'/api/auth/login',{userName:'admin',password:'correct horse battery staple'});
let m=await post(adm.request,'/api/auth/mfa',{code:totp(st.key)}); if(m.status!==200){await new Promise(r=>setTimeout(r,31000));m=await post(adm.request,'/api/auth/mfa',{code:totp(st.key)});}
await post(adm.request,'/api/admin/users',{userName:'rec.tui',displayName:'Tui Reception',roles:['Reception / Booking'],password:'correct horse battery staple'});
await post(ctx.request,'/api/auth/login',{userName:'rec.tui',password:'correct horse battery staple'});
const e=await post(ctx.request,'/api/auth/enrol/begin',{});await post(ctx.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});
const rp=await ctx.newPage();rp.on('pageerror',x=>errs.push(x.message));rp.on('console',x=>{if(x.type()==='error')errs.push(x.text())});
await rp.goto(BASE+'/waitlist');await rp.waitForSelector('table');
await rp.click('td button:has-text("Book")');await rp.waitForSelector('.choose-list');await rp.click('.choice >> nth=0');await shot(rp,'11-book-panel');
await rp.click('.detail-row button.btn-primary:has-text("Book")');await rp.waitForTimeout(800);
if(await rp.locator('button:has-text("Book anyway")').count()){await shot(rp,'11b-warnings');await rp.click('button:has-text("Book anyway")');}
await rp.waitForSelector('.notice');await shot(rp,'12-booked');console.log('booked:',await rp.locator('.notice').textContent());
const pid=(await (await ctx.request.get(BASE+'/api/patients?q=ZSC21TN',{headers:{'X-BrightPAS-Request':'1'}})).json())[0].id;
await rp.goto(BASE+'/patients/'+pid);await rp.waitForTimeout(1000);await shot(rp,'13-patient');
await rp.goto(BASE+'/');await rp.waitForTimeout(800);await shot(rp,'15-home-rec');
const ap=await adm.newPage();await ap.setViewportSize({width:1366,height:900});await ap.goto(BASE+'/admin/licence');await ap.waitForSelector('.module-grid');await shot(ap,'14-modules-after');
await ap.goto(BASE+'/');await ap.waitForTimeout(600);await shot(ap,'16-home-admin');
console.log('ERRORS:',errs);await b.close();})().catch(e=>{console.error(e);process.exit(1)});
