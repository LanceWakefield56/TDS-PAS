const {chromium}=require('playwright');const {BASE,post,totp}=require('./lib');
(async()=>{const b=await chromium.launch();const st=JSON.parse(require('fs').readFileSync('state.json'));const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1366,height:900}});
await post(ctx.request,'/api/auth/login',{userName:'admin',password:'correct horse battery staple'});let m=await post(ctx.request,'/api/auth/mfa',{code:totp(st.key)});
if(m.status!==200){await new Promise(r=>setTimeout(r,31000));m=await post(ctx.request,'/api/auth/mfa',{code:totp(st.key)});}
const p=await ctx.newPage();p.on('response',r=>{if(r.url().includes('/api/'))console.log(r.request().method(),r.url().replace(BASE,''),r.status())});
await p.goto(BASE+'/theatre/templates');await p.waitForTimeout(1500);await p.click('button:has-text("Add template")');
await p.locator('label:has-text("Surgeon / endoscopist") input').fill('Dr Ben Hall');await p.click('button:has-text("Save template")');await p.waitForTimeout(1500);
console.log(await p.locator('.error').allTextContents());await p.screenshot({path:'/tmp/dbg.png',fullPage:true});await b.close();})();
