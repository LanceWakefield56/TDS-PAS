const {chromium}=require('playwright');const {adminContext,BASE,post,totp,H}=require('./lib');
const shot=async(p,n)=>{await p.waitForTimeout(400);await p.screenshot({path:`/tmp/e2e-${n}.png`,fullPage:true});console.log('shot',n);};
async function userContext(b,admin,userName,role){
  const pw='correct horse battery staple';
  const c=await post(admin.request,'/api/admin/users',{userName,displayName:userName,roles:[role],password:pw});
  if(c.status!==200&&!JSON.stringify(c.json).includes('exist')) throw new Error('create '+JSON.stringify(c));
  const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1366,height:900}});
  const l=await post(ctx.request,'/api/auth/login',{userName,password:pw});
  const e=await post(ctx.request,'/api/auth/enrol/begin',{});const d=await post(ctx.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});
  if(d.json?.next!=='done') throw new Error('enrol '+JSON.stringify([l,e,d]));
  return ctx;
}
(async()=>{const b=await chromium.launch();const admin=await adminContext(b,process.argv[2]);const p=await admin.newPage();
const errs=[];p.on('console',m=>{if(m.type()==='error')errs.push(m.text())});p.on('pageerror',e=>errs.push(e.message));
await p.goto(BASE+'/admin/licence');await p.waitForSelector('h1');await shot(p,'01-licence-none');
await p.setInputFiles('#licfile','/home/claude/repo/TDS-PAS/licensing/sandbox/BrightPAS-Sandbox-Licence.lic');
await p.waitForSelector('.notice',{timeout:10000});await shot(p,'02-licence-loaded');
console.log('notice:',await p.locator('.notice').first().textContent());
await p.click('a:has-text("Set up") >> nth=0').catch(()=>{});
await p.goto(BASE+'/admin/modules/referrals');await p.waitForSelector('.setup-steps');await shot(p,'03-wizard-referrals');
await p.click('a:has-text("Review targets")');await p.waitForSelector('h1:has-text("Waiting-time targets")');await p.click('button:has-text("Confirm targets")');
await p.waitForSelector('.setup-steps');
const rec=await userContext(b,admin,'rec.jane','Reception / Booking');const dr=await userContext(b,admin,'dr.hall','Surgeon / Endoscopist');
await p.click('button:has-text("Check again")');await p.waitForTimeout(600);await shot(p,'04-wizard-ready');
await p.click('button:has-text("Finish set-up")');await p.waitForSelector('.notice');await shot(p,'05-after-finish');
// theatre wizard
await p.goto(BASE+'/admin/modules/theatre');await p.waitForSelector('.setup-steps');await p.click('button:has-text("Finish set-up")');await p.waitForSelector('.notice');
// reminders wizard (shows required steps)
await p.goto(BASE+'/admin/modules/reminders');await p.waitForSelector('.setup-steps');await shot(p,'06-wizard-reminders');
// reception: patient, theatre list, referral via UI
const rp=await rec.newPage();rp.on('pageerror',e=>errs.push('rec: '+e.message));
const pt=await post(rec.request,'/api/patients',{familyName:'Ngātai',givenNames:'Aroha',dateOfBirth:'1980-05-17',sex:'Female',nhi:'ZSC21TN',confirmedNotDuplicate:true});
const locs=await (await rec.request.get(BASE+'/api/theatre/locations',{headers:H})).json();
const d=new Date();d.setDate(d.getDate()+10);const day=d.toISOString().slice(0,10);
const ses=await post(rec.request,'/api/theatre/sessions',{locationId:locs[0].id,date:day,startTime:'08:00:00',endTime:'12:00:00',type:'Surgical',surgeon:'Dr Ben Hall',anaesthetist:null,notes:null});
console.log('patient',pt.status,'session',ses.status);
await rp.goto(BASE+'/');await rp.waitForSelector('nav');console.log('rec nav:',(await rp.locator('nav a').allTextContents()).join(' | '));
await rp.goto(BASE+'/referrals');await rp.click('button:has-text("New referral")');
await rp.fill('input[placeholder^="e.g. ZSC21TN"]','ZSC21TN');await rp.click('button:has-text("Find")');await rp.click('.picker-results button');
await rp.fill('input[placeholder="e.g. Dr Mere Smith"]','Dr Mere Smith');
await rp.locator('label:has-text("Referred to") input').fill('Dr Ben Hall');
await rp.locator('label:has-text("Reason for referral") textarea').fill('Symptomatic gallstones, three episodes of biliary colic.');
await shot(rp,'07-new-referral');await rp.click('button:has-text("Save referral")');await rp.waitForSelector('.notice');await shot(rp,'08-referral-saved');
// surgeon triage via UI
const dp=await dr.newPage();dp.on('pageerror',e=>errs.push('dr: '+e.message));
await dp.goto(BASE+'/referrals');await dp.click('tbody tr >> nth=0');await dp.click('button:has-text("Accept – add to waitlist")');
await dp.locator('label:has-text("Planned procedure") input').fill('Laparoscopic cholecystectomy');
await dp.locator('label:has-text("Priority") select').selectOption('SemiUrgent');
await shot(dp,'09-triage');await dp.click('button:has-text("Accept") >> nth=-1');await dp.waitForSelector('.notice');
// reception books from waitlist
await rp.goto(BASE+'/waitlist');await rp.waitForSelector('table');await shot(rp,'10-waitlist');
await rp.click('button:has-text("Book")');await rp.waitForSelector('.choose-list');await rp.click('.choice >> nth=0');await shot(rp,'11-book-panel');
await rp.click('.detail-row button:has-text("Book")');await rp.waitForTimeout(800);
if(await rp.locator('button:has-text("Book anyway")').count()){await shot(rp,'11b-warnings');await rp.click('button:has-text("Book anyway")');}
await rp.waitForSelector('.notice');await shot(rp,'12-booked');console.log('booked:',await rp.locator('.notice').textContent());
await rp.goto(BASE+'/patients/'+pt.json.id);await rp.waitForTimeout(800);await shot(rp,'13-patient');
await p.goto(BASE+'/admin/licence');await p.waitForSelector('.module-grid');await shot(p,'14-modules-after');
console.log('ERRORS:',errs);await b.close();})().catch(e=>{console.error(e);process.exit(1)});
