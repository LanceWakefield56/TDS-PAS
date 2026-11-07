const {chromium}=require('playwright');const {adminContext,BASE,post,totp,H}=require('./lib');
const shot=async(p,n)=>{await p.waitForTimeout(400);await p.screenshot({path:`/tmp/e3-${n}.png`,fullPage:true});console.log('shot',n);};
async function userCtx(b,admin,userName,role){const pw='correct horse battery staple';
  await post(admin.request,'/api/admin/users',{userName,displayName:userName.replace('.',' '),roles:[role],password:pw});
  const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1366,height:900}});
  await post(ctx.request,'/api/auth/login',{userName,password:pw});const e=await post(ctx.request,'/api/auth/enrol/begin',{});
  const d=await post(ctx.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});if(d.json?.next!=='done')throw new Error('enrol');return ctx;}
(async()=>{const b=await chromium.launch();const admin=await adminContext(b,process.argv[2]);const p=await admin.newPage();const errs=[];
const watch=(pg,n)=>{pg.on('pageerror',e=>errs.push(n+': '+e.message));pg.on('console',m=>{if(m.type()==='error')errs.push(n+': '+m.text())});};watch(p,'admin');
await post(admin.request,'/api/admin/licence',{text:require('fs').readFileSync('/home/claude/repo/TDS-PAS/licensing/sandbox/BrightPAS-Sandbox-Licence.lic','utf8')});
// Organisation: details
await p.goto(BASE+'/admin/organisation');await p.waitForSelector('form');
await p.locator('label:has-text("Street address") input').fill('12 Example Street');await p.locator('label:has-text("Town / city") input').fill('Nelson');
await p.locator('label:has-text("Postcode") input').fill('7010');await p.locator('label:has-text("Phone") input').fill('03 555 0100');
await p.click('button:has-text("Save clinic details")');await p.waitForSelector('.notice');await shot(p,'01-org-details');
// rooms: activate TH2
await p.click('button:has-text("Theatres & rooms")');await p.waitForSelector('table');await p.click('tr:has-text("TH2") button:has-text("Edit")');
await p.locator('.card.subtle label:has-text("Name") input').fill('Theatre 2');await p.check('.card.subtle input[type=checkbox]');await p.click('.card.subtle button:has-text("Save")');await p.waitForTimeout(600);await shot(p,'02-rooms');
// clinicians
await p.click('button:has-text("Clinicians")');await p.click('button:has-text("Add clinician")');
await p.locator('label:has-text("Name (as on") input').fill('Dr Ben Hall');await p.locator('label:has-text("Specialty") input').fill('General surgery');await p.locator('label:has-text("HPI CPN") input').fill('12ABCD');
await p.click('.card.subtle button:has-text("Save")');await p.waitForTimeout(500);
await post(admin.request,'/api/admin/organisation/clinicians',{name:'Dr Kate Ford',type:'Anaesthetist',isActive:true});
await post(admin.request,'/api/admin/organisation/clinicians',{name:'Dr Ana Lee',type:'Endoscopist',specialty:'Gastroenterology',isActive:true});
await p.click('button:has-text("Procedures")');await p.click('button:has-text("Clinicians")');await p.waitForSelector('table');await shot(p,'03-clinicians');
await p.click('button:has-text("Procedures")');await p.click('button:has-text("Add example procedures")');await p.waitForSelector('table');await shot(p,'04-procedures');
// roles
await p.goto(BASE+'/admin/roles');await p.waitForSelector('.role-list');await p.click('.role-list button:has-text("System Administrator")');await shot(p,'05-roles');
// templates
await p.goto(BASE+'/theatre/templates');await p.click('button:has-text("Add template")');await p.locator('label:has-text("Surgeon / endoscopist") input').fill('Dr Ben Hall');
await p.locator('label:has-text("Anaesthetist") input').fill('Dr Kate Ford');await p.click('button:has-text("Save template")');await p.waitForSelector('table');
await p.click('button:has-text("Create lists")');await p.waitForSelector('.notice');await shot(p,'06-templates');
// letters admin
await p.goto(BASE+'/admin/letters?tab=practices');await p.click('button:has-text("Add practice")');
await p.locator('label:has-text("Practice name") input').fill('Stoke Medical Centre');await p.locator('label:has-text("HPI facility number") input').fill('F12345-A');
await p.locator('label:has-text("HealthLink EDI") input').fill('stokemed');await p.locator('label:has-text("Town / city") input').fill('Nelson');
await p.click('.card.subtle button:has-text("Save")');await p.waitForTimeout(500);await shot(p,'07-practices');
await p.goto(BASE+'/admin/letters');await p.waitForSelector('table');await p.click('tr:has-text("Appointment letter") button:has-text("Edit")');await shot(p,'08-template-edit');
// reception: patient, book with catalogue, letter
const rec=await userCtx(b,admin,'sam.reception','Reception / Booking');
for(const m of ['theatre','letters']){const r=await post(admin.request,`/api/admin/modules/${m}/setup/complete`,{});console.log('setup',m,r.status,JSON.stringify(r.json));}
const rp=await rec.newPage();watch(rp,'rec');
const pt=await post(rec.request,'/api/patients',{familyName:'Ngātai',givenNames:'Aroha',dateOfBirth:'1980-05-17',sex:'Female',nhi:'ZSC21TN',confirmedNotDuplicate:true,addressLine1:'1 Test Road',city:'Nelson',postcode:'7010'});
await rp.goto(BASE+'/patients/'+pt.json.id);await rp.click('button:has-text("Edit")');await rp.locator('label:has-text("GP practice") select').selectOption('F12345-A');
await rp.click('button:has-text("Save")');await rp.waitForTimeout(600);
const sess=await (await rec.request.get(BASE+'/api/theatre/sessions?from=2026-11-08&to=2026-12-31',{headers:H})).json();console.log('lists from templates:',sess.length);
const day=sess[0].date;await rp.goto(BASE+'/theatre?date='+day+'&patient='+pt.json.id);await rp.click('button:has-text("Add booking")');
await rp.locator('label:has-text("Procedure") input').fill('Inguinal hernia repair');await rp.waitForTimeout(300);await shot(rp,'09-booking-catalogue');
await rp.locator('.card.subtle label:has-text("Side") select').selectOption('Right');await rp.click('.card.subtle button:has-text("Book")');await rp.waitForTimeout(800);
await rp.goto(BASE+'/patients/'+pt.json.id);await rp.waitForTimeout(800);await rp.click('a:has-text("Write a letter")');
await rp.locator('label:has-text("Template") select').selectOption('GPBOOK');await rp.locator('label:has-text("Booking this letter") select').selectOption({index:1});
await rp.click('button:has-text("Create draft")');await rp.waitForSelector('textarea.letter-edit');await shot(rp,'10-letter-draft');
await rp.click('button:has-text("Finalise")');await rp.waitForSelector('.letter-sheet');await shot(rp,'11-letter-final');
await rp.click('button:has-text("via HealthLink")');await rp.waitForSelector('.notice');await shot(rp,'12-letter-sent');
await rp.goto(BASE+'/patients/'+pt.json.id);await rp.waitForTimeout(800);await shot(rp,'13-patient');
await p.goto(BASE+'/admin/modules/letters');await p.waitForSelector('.setup-steps');await shot(p,'14-wizard-letters');
console.log('ERRORS',errs);await b.close();})().catch(e=>{console.error(e);process.exit(1)});
