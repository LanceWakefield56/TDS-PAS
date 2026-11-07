const {chromium}=require('playwright');const {adminContext,BASE,post,totp,H}=require('./lib');const fs=require('fs');
const shot=async(p,n)=>{await p.waitForTimeout(500);await p.screenshot({path:`/tmp/e4-${n}.png`,fullPage:true});console.log('shot',n);};
async function userCtx(b,admin,userName,display,role){const pw='correct horse battery staple';
  await post(admin.request,'/api/admin/users',{userName,displayName:display,roles:[role],password:pw});
  const ctx=await b.newContext({ignoreHTTPSErrors:true,viewport:{width:1500,height:1000}});
  await post(ctx.request,'/api/auth/login',{userName,password:pw});const e=await post(ctx.request,'/api/auth/enrol/begin',{});
  const d=await post(ctx.request,'/api/auth/enrol/complete',{code:totp(e.json.sharedKey)});if(d.json?.next!=='done')throw new Error('enrol '+userName);return ctx;}
const nzToday=()=>new Date().toLocaleDateString('en-CA',{timeZone:'Pacific/Auckland'});
const monday=(iso)=>{const d=new Date(iso+'T00:00Z');const off=(d.getUTCDay()+6)%7;d.setUTCDate(d.getUTCDate()-off);return d.toISOString().slice(0,10);};
const add=(iso,n)=>{const d=new Date(iso+'T00:00Z');d.setUTCDate(d.getUTCDate()+n);return d.toISOString().slice(0,10);};
(async()=>{const b=await chromium.launch();const admin=await adminContext(b,process.argv[2]);const errs=[];
const watch=(pg,n)=>{pg.on('pageerror',e=>errs.push(n+': '+e.message));pg.on('console',m=>{if(m.type()==='error')errs.push(n+': '+m.text())});};
await post(admin.request,'/api/admin/licence',{text:fs.readFileSync('/home/claude/repo/TDS-PAS/licensing/sandbox/BrightPAS-Sandbox-Licence.lic','utf8')});
for(const c of [['Dr Ben Hall','Surgeon'],['Dr Kate Ford','Anaesthetist'],['Dr Ana Lee','Endoscopist'],['Dr Mia Grant','Surgeon']]) await post(admin.request,'/api/admin/organisation/clinicians',{name:c[0],type:c[1],isActive:true});
await post(admin.request,'/api/admin/organisation/procedures/examples',{});
const rec=await userCtx(b,admin,'sam.reception','Sam Reception','Reception / Booking');
const mgr=await userCtx(b,admin,'lynda.roster','Lynda Roster','Roster Manager');
const nurse=await userCtx(b,admin,'jacqui.nurse','Jacqui Nurse','Nurse');
const mon=monday(nzToday());
// staff
const staffIds={};for(const [n,r,u,h] of [['Mara','Nurse',null,40],['Jacqui','Nurse','jacqui.nurse',32],['Jackie','Nurse',null,32],['Teresa','Nurse',null,24],['Kate','AnaestheticTechnician',null,40],['Lance','Administration',null,null],['Lynda','Manager','lynda.roster',null],['Caroline','Administration',null,null]]){
  const r2=await post(mgr.request,'/api/roster/staff',{name:n,role:r,userName:u,contractedHours:h,isActive:true});staffIds[n]=r2.json.id;}
for(const n of ['Lance','Lynda','Caroline']) for(let d=0;d<5;d++) await post(mgr.request,'/api/roster/templates',{staffId:staffIds[n],dayOfWeek:['Monday','Tuesday','Wednesday','Thursday','Friday'][d],allDay:true,area:'Office',everyWeeks:1,startsOn:mon,isActive:true,breakMinutes:0});
for(const [n,s,e] of [['Mara','07:30','16:00'],['Jacqui','08:00','16:30'],['Jackie','08:00','17:00'],['Teresa','08:00','17:00'],['Kate','09:00','18:00']]) for(const day of ['Tuesday','Wednesday','Thursday'])
  await post(mgr.request,'/api/roster/templates',{staffId:staffIds[n],dayOfWeek:day,allDay:false,start:s+':00',end:e+':00',breakMinutes:30,area:n==='Kate'?'Anaesthetics':'Theatre',everyWeeks:1,startsOn:mon,isActive:true});
console.log('gen',JSON.stringify((await post(mgr.request,'/api/roster/templates/generate',{from:mon,to:add(mon,13)})).json).slice(0,80));
for(const m of ['theatre','rostering']){const r=await post(admin.request,`/api/admin/modules/${m}/setup/complete`,{});console.log('setup',m,r.status,JSON.stringify(r.json));}
// theatre lists + bookings
const locs=await (await rec.request.get(BASE+'/api/theatre/locations',{headers:H})).json();
const tue=add(mon,1),wed=add(mon,2),thu=add(mon,3);
const s1=await post(rec.request,'/api/theatre/sessions',{locationId:locs[0].id,date:wed,startTime:'08:00:00',endTime:'12:30:00',type:'Endoscopy',surgeon:'Dr Ana Lee',anaesthetist:null,notes:null});
const s2=await post(rec.request,'/api/theatre/sessions',{locationId:locs[0].id,date:thu,startTime:'08:00:00',endTime:'16:00:00',type:'Surgical',surgeon:'Dr Mia Grant',anaesthetist:'Dr Kate Ford',notes:null});
const names=[['Carpenter','Sarah','ZAA0024'],['Benjamin','Kristi','ZAA0067'],['Volmer','Eleanor','ZAA0105'],['Kitney','Kirstin','ZAA0148'],['Jordan','Melissa','ZAA0180']];
const pts=[];for(const [f,g] of names){const r=await post(rec.request,'/api/patients',{familyName:f,givenNames:g,dateOfBirth:'1975-03-0'+(pts.length+1),sex:'Female',confirmedNotDuplicate:true,mobilePhone:'0215550'+(100+pts.length)});pts.push(r.json.id);}
for(let i=0;i<3;i++) await post(rec.request,'/api/theatre/bookings',{sessionId:s1.json.id,patientId:pts[i],procedure:['Colonoscopy','Gastroscopy','Gastroscopy and colonoscopy'][i],laterality:'NotApplicable',durationMinutes:[30,15,45][i],anaesthetic:'Sedation',notes:null,acceptWarnings:true,isTentative:i===2,payment:'Insurer'});
for(let i=3;i<5;i++) await post(rec.request,'/api/theatre/bookings',{sessionId:s2.json.id,patientId:pts[i],procedure:i===3?'Laparoscopic cholecystectomy':'Inguinal hernia repair',laterality:i===3?'NotApplicable':'Right',durationMinutes:i===3?75:60,anaesthetic:'General',notes:null,acceptWarnings:true,isTentative:i===4});
for(const [t,k,c,nc] of [['17:00','FollowUp','Dr Mia Grant',true],['17:30','Consultation','Dr Mia Grant',false],['18:00','PhoneCall','Dr Mia Grant',true],['13:00','Consultation','Dr Ben Hall',false]])
  await post(rec.request,'/api/theatre/appointments',{patientId:pts[0],date:thu,startTime:t+':00',durationMinutes:k==='PhoneCall'?10:20,kind:k,clinician:c,noCharge:nc,isTentative:false,notes:null,acceptWarnings:true});
// UI: calendar
const rp=await rec.newPage();watch(rp,'rec');await rp.goto(BASE+'/calendar?date='+wed);await rp.waitForSelector('.cal');await shot(rp,'01-calendar-week');
await rp.goto(BASE+'/calendar?date='+thu+'&view=day');await rp.waitForSelector('.cal');await shot(rp,'02-calendar-day');
// new appointment by clicking a slot
await rp.click('.cal-slot >> nth=24');await rp.waitForSelector('h2:has-text("New appointment")');
await rp.fill('input[placeholder^="e.g. ZSC21TN"]','Volmer');await rp.click('button:has-text("Find")');await rp.click('.picker-results button');
await rp.locator('label:has-text("Clinician") input').fill('Dr Ben Hall');await rp.check('label:has-text("No charge") input');
await rp.click('section button:has-text("Book")');await rp.waitForTimeout(800);if(await rp.locator('button:has-text("Book anyway")').count())await rp.click('button:has-text("Book anyway")');
await rp.waitForTimeout(800);await rp.click('.cal-item:has-text("VOLMER")');await rp.waitForSelector('.side-panel');await shot(rp,'03-calendar-appt-selected');
// booking details
await rp.goto(BASE+'/theatre?date='+wed);await rp.waitForSelector('table');await rp.click('button:has-text("Details") >> nth=0');
await rp.check('label:has-text("Consent form signed") input');await rp.check('label:has-text("Pre-admission questionnaire") input');
await rp.locator('label:has-text("Scope ID") input').fill('CF-HQ190-2');await rp.locator('label:has-text("Theatre time in") input').fill('08:05');await rp.locator('label:has-text("Theatre time out") input').fill('08:35');
await shot(rp,'04-booking-details');await rp.click('button:has-text("Save details")');await rp.waitForTimeout(800);await shot(rp,'05-list-after-details');
// roster
const mp=await mgr.newPage();watch(mp,'mgr');await mp.goto(BASE+'/roster?date='+wed);await mp.waitForSelector('.cal');await shot(mp,'06-roster-week');
await mp.goto(BASE+'/roster?date='+thu+'&view=day');await mp.waitForSelector('.cal');await shot(mp,'07-roster-day');
// tentative shift via slot
await mp.click('.cal-slot >> nth=27');await mp.waitForSelector('h2:has-text("New shift")');await mp.locator('.side-panel label:has-text("Staff") select').selectOption({label:'Teresa (Nurse)'});
await mp.check('.side-panel label:has-text("Tentative") input');await mp.locator('.side-panel label:has-text("Note") input').fill('if needed');await mp.click('button:has-text("Save shift")');await mp.waitForTimeout(800);
if(await mp.locator('button:has-text("Save anyway")').count()){await shot(mp,'07b-warn');await mp.click('button:has-text("Save anyway")');await mp.waitForTimeout(600);}
// nurse requests leave
const np=await nurse.newPage();watch(np,'nurse');await np.goto(BASE+'/roster?tab=leave');await np.waitForSelector('h2:has-text("Request leave")');
await np.locator('label:has-text("First day") input').fill(add(mon,8));await np.locator('label:has-text("Last day") input').fill(add(mon,10));await np.click('button:has-text("Request leave")');await np.waitForSelector('.notice');await shot(np,'08-nurse-leave');
await mp.goto(BASE+'/roster?tab=leave');await mp.waitForSelector('table');await mp.click('td button:has-text("Approve")');await mp.waitForSelector('.notice');await shot(mp,'09-leave-approved');
await mp.goto(BASE+'/roster?tab=hours');await mp.waitForSelector('table');await shot(mp,'10-hours');
await mp.goto(BASE+'/roster?date='+add(mon,8));await mp.waitForSelector('.cal');await shot(mp,'11-roster-next-week');
await np.goto(BASE+'/roster');await np.waitForSelector('.cal');await shot(np,'12-nurse-roster');
console.log('ERRORS',errs);await b.close();})().catch(e=>{console.error(e);process.exit(1)});
