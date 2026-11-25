import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, ApiError, errText, fileToBase64, formatDate, nzDateTime } from '../api';

/** Bookings from specialists' rooms (0.28.0): the request form, list and detail, shared by the specialist portal and the booking office. */

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const num = (v: string) => (v.trim() === '' ? null : Number(v));
const CATEGORIES = [['Referral', 'Referral letter'], ['Consent', 'Signed consent'], ['Clinical', 'Results / clinical'], ['Insurance', 'Insurer approval'], ['Other', 'Other']];
export const STATUS_PILL: Record<string, string> = { Submitted: 'blue', Query: 'amber', Accepted: 'green', Declined: 'red', Withdrawn: 'grey' };
const STATUS_TEXT: Record<string, string> = { Submitted: 'With TDS', Query: 'Question for the rooms', Accepted: 'Accepted', Declined: 'Declined', Withdrawn: 'Withdrawn' };
const FUNDERS = [['Insurer', 'Health insurer'], ['Acc', 'ACC'], ['SelfPay', 'Self-funded'], ['HealthNz', 'Health NZ (publicly funded)'], ['Other', 'Other']];

interface Opts { specialists: { id: string; name: string; practiceName?: string; phone?: string; email?: string }[]; procedures: { id: string; code: string; name: string; service: string; defaultMinutes: number; defaultAnaesthetic: string }[]; instructions?: string; maxFileMegabytes: number; maxFiles: number }
interface Pending { category: string; fileName: string; contentBase64: string; size: number }

const blank = {
  specialistId: '', roomsName: '', contactName: '', contactPhone: '', contactEmail: '',
  nhi: '', familyName: '', givenNames: '', dateOfBirth: '', sex: 'Female', mobilePhone: '', email: '', addressLine1: '', suburb: '', city: '', postcode: '', interpreterLanguage: '',
  service: 'Surgical', procedureItemId: '', procedure: '', laterality: 'NotApplicable', anaesthetic: 'General', durationMinutes: '', priority: 'Routine', funder: 'Insurer', funderReference: '', insurerName: '',
  earliestDate: '', latestDate: '', availability: '', clinicalNotes: '',
  weightKg: '', heightCm: '', asa: '', diabetic: false, sleepApnoea: false, anticoagulated: false, anticoagulantDetails: '', allergies: '', comorbidities: '',
  responsibleAdultArranged: false, consentSigned: false, consentSignedOn: '',
};

/** The booking request form. base is /api/ebookings/rooms (specialist portal) or /api/ebookings (booking office entering a phoned or faxed request). */
export function BookingRequestForm({ base, onDone }: { base: string; onDone: (id: string, number: string) => void }) {
  const [opts, setOpts] = useState<Opts | null>(null);
  const [f, setF] = useState({ ...blank });
  const [files, setFiles] = useState<Pending[]>([]);
  const [fileCat, setFileCat] = useState('Referral');
  const [errors, setErrors] = useState<string[]>([]);
  const [confirm, setConfirm] = useState<string[] | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    api<Opts>(`${base}/options`).then((o) => {
      setOpts(o);
      if (o.specialists.length === 1) { const s = o.specialists[0]; setF((x) => ({ ...x, specialistId: s.id, roomsName: s.practiceName ?? '', contactPhone: s.phone ?? '', contactEmail: s.email ?? '' })); }
    }).catch((e) => setErrors(errs(e)));
  }, [base]);
  const set = (k: keyof typeof blank, v: string | boolean) => setF((x) => ({ ...x, [k]: v }));
  const pickSpecialist = (id: string) => {
    const s = opts?.specialists.find((x) => x.id === id);
    setF((x) => ({ ...x, specialistId: id, roomsName: x.roomsName || s?.practiceName || '', contactPhone: x.contactPhone || s?.phone || '', contactEmail: x.contactEmail || s?.email || '' }));
  };
  const pickProcedure = (id: string) => {
    const p = opts?.procedures.find((x) => x.id === id);
    setF((x) => ({ ...x, procedureItemId: id, procedure: p ? p.name : x.procedure, durationMinutes: p ? String(p.defaultMinutes) : x.durationMinutes, anaesthetic: p?.defaultAnaesthetic ?? x.anaesthetic, service: p?.service ?? x.service }));
  };
  const addFile = async (file: File | undefined) => {
    if (!file) return;
    if (opts && file.size > opts.maxFileMegabytes * 1024 * 1024) { setErrors([`${file.name} is larger than ${opts.maxFileMegabytes} MB.`]); return; }
    if (files.reduce((n, p) => n + p.size, 0) + file.size > 40 * 1024 * 1024) { setErrors(['The files together must be 40 MB or smaller – send the request, then add large scans one at a time.']); return; }
    setFiles((x) => [...x, { category: fileCat, fileName: file.name, size: file.size, contentBase64: '' }]);
    const b64 = await fileToBase64(file);
    setFiles((x) => x.map((p) => (p.fileName === file.name && p.contentBase64 === '' ? { ...p, contentBase64: b64 } : p)));
  };
  const submit = async (confirmDuplicate: boolean) => {
    setErrors([]); setBusy(true);
    try {
      const body = {
        ...f, specialistId: f.specialistId || '00000000-0000-0000-0000-000000000000', procedureItemId: f.procedureItemId || null, dateOfBirth: f.dateOfBirth || null,
        durationMinutes: num(f.durationMinutes), weightKg: num(f.weightKg), heightCm: num(f.heightCm), asa: num(f.asa),
        earliestDate: f.earliestDate || null, latestDate: f.latestDate || null, consentSignedOn: f.consentSigned ? (f.consentSignedOn || null) : null,
        files: files.map(({ category, fileName, contentBase64 }) => ({ category, fileName, contentBase64 })), confirmDuplicate,
      };
      const r = await api<{ id: string; number: string }>(base, { body });
      setConfirm(null); setF({ ...blank }); setFiles([]);
      onDone(r.id, r.number);
    } catch (e) {
      if (e instanceof ApiError && e.body?.needsConfirm) setConfirm(e.body.warnings as string[]);
      else setErrors(errs(e));
    } finally { setBusy(false); }
  };
  if (!opts) return errors.length ? <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div> : <p>Loading…</p>;
  if (opts.specialists.length === 0) return <p className="warn-box">Your account is not linked to a specialist yet – ask the TDS administrator to link you.</p>;
  const bmi = num(f.weightKg) && num(f.heightCm) ? (Number(f.weightKg) / ((Number(f.heightCm) / 100) ** 2)).toFixed(1) : null;
  return (
    <div className="stack">
      {opts.instructions && <p className="hint">{opts.instructions}</p>}
      <fieldset className="stack"><legend>Specialist and rooms</legend>
        <div className="form-grid">
          <label>Specialist<select value={f.specialistId} onChange={(e) => pickSpecialist(e.target.value)}><option value="">Choose…</option>{opts.specialists.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
          <label>Rooms / practice<input value={f.roomsName} onChange={(e) => set('roomsName', e.target.value)} /></label>
          <label>Contact person<input value={f.contactName} onChange={(e) => set('contactName', e.target.value)} autoComplete="name" /></label>
          <label>Contact phone<input value={f.contactPhone} onChange={(e) => set('contactPhone', e.target.value)} type="tel" /></label>
          <label>Contact email<input value={f.contactEmail} onChange={(e) => set('contactEmail', e.target.value)} type="email" /></label>
        </div>
      </fieldset>
      <fieldset className="stack"><legend>Patient</legend>
        <div className="form-grid">
          <label>NHI<input value={f.nhi} onChange={(e) => set('nhi', e.target.value.toUpperCase())} maxLength={7} /></label>
          <label>Family name<input value={f.familyName} onChange={(e) => set('familyName', e.target.value)} /></label>
          <label>Given names<input value={f.givenNames} onChange={(e) => set('givenNames', e.target.value)} /></label>
          <label>Date of birth<input type="date" value={f.dateOfBirth} onChange={(e) => set('dateOfBirth', e.target.value)} /></label>
          <label>Sex<select value={f.sex} onChange={(e) => set('sex', e.target.value)}><option value="Female">Female</option><option value="Male">Male</option><option value="Indeterminate">Indeterminate</option><option value="Unknown">Not known</option></select></label>
          <label>Patient mobile<input value={f.mobilePhone} onChange={(e) => set('mobilePhone', e.target.value)} type="tel" /></label>
          <label>Patient email<input value={f.email} onChange={(e) => set('email', e.target.value)} type="email" /></label>
          <label>Street address<input value={f.addressLine1} onChange={(e) => set('addressLine1', e.target.value)} /></label>
          <label>Suburb<input value={f.suburb} onChange={(e) => set('suburb', e.target.value)} /></label>
          <label>Town / city<input value={f.city} onChange={(e) => set('city', e.target.value)} /></label>
          <label>Postcode<input value={f.postcode} onChange={(e) => set('postcode', e.target.value)} inputMode="numeric" /></label>
          <label>Interpreter language (if needed)<input value={f.interpreterLanguage} onChange={(e) => set('interpreterLanguage', e.target.value)} /></label>
        </div>
      </fieldset>
      <fieldset className="stack"><legend>Procedure and funding</legend>
        <div className="form-grid">
          <label>From TDS's procedure list<select value={f.procedureItemId} onChange={(e) => pickProcedure(e.target.value)}><option value="">(type it below)</option>{opts.procedures.map((p) => <option key={p.id} value={p.id}>{p.name}{p.code ? ` (${p.code})` : ''}</option>)}</select></label>
          <label className="span-all">Procedure<input value={f.procedure} onChange={(e) => set('procedure', e.target.value)} /></label>
          <label>Side<select value={f.laterality} onChange={(e) => set('laterality', e.target.value)}><option value="NotApplicable">Not applicable</option><option value="Left">Left</option><option value="Right">Right</option><option value="Bilateral">Both</option></select></label>
          <label>Anaesthetic<select value={f.anaesthetic} onChange={(e) => set('anaesthetic', e.target.value)}>{['General', 'Sedation', 'Regional', 'Local', 'None'].map((a) => <option key={a} value={a}>{a}</option>)}</select></label>
          <label>Expected minutes<input value={f.durationMinutes} onChange={(e) => set('durationMinutes', e.target.value)} inputMode="numeric" /></label>
          <label>Service<select value={f.service} onChange={(e) => set('service', e.target.value)}><option value="Surgical">Surgical</option><option value="Endoscopy">Endoscopy</option></select></label>
          <label>Urgency<select value={f.priority} onChange={(e) => set('priority', e.target.value)}><option value="Routine">Routine</option><option value="SemiUrgent">Semi-urgent</option><option value="Urgent">Urgent</option></select></label>
          <label>Funding<select value={f.funder} onChange={(e) => set('funder', e.target.value)}>{FUNDERS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          {f.funder === 'Insurer' && <label>Insurer<input value={f.insurerName} onChange={(e) => set('insurerName', e.target.value)} /></label>}
          <label>{f.funder === 'Acc' ? 'ACC claim number' : f.funder === 'Insurer' ? 'Approval number' : 'Funding reference'}<input value={f.funderReference} onChange={(e) => set('funderReference', e.target.value)} /></label>
          <label>Earliest date<input type="date" value={f.earliestDate} onChange={(e) => set('earliestDate', e.target.value)} /></label>
          <label>Latest date<input type="date" value={f.latestDate} onChange={(e) => set('latestDate', e.target.value)} /></label>
          <label className="span-all">Dates or days to avoid<input value={f.availability} onChange={(e) => set('availability', e.target.value)} /></label>
          <label className="span-all">Clinical notes for TDS<textarea rows={3} value={f.clinicalNotes} onChange={(e) => set('clinicalNotes', e.target.value)} /></label>
        </div>
      </fieldset>
      <fieldset className="stack"><legend>Before surgery</legend>
        <div className="form-grid">
          <label>Weight (kg)<input value={f.weightKg} onChange={(e) => set('weightKg', e.target.value)} inputMode="decimal" /></label>
          <label>Height (cm)<input value={f.heightCm} onChange={(e) => set('heightCm', e.target.value)} inputMode="decimal" /></label>
          <label>ASA grade<select value={f.asa} onChange={(e) => set('asa', e.target.value)}><option value="">Not known</option>{[1, 2, 3, 4, 5].map((n) => <option key={n} value={n}>ASA {n}</option>)}</select></label>
          {bmi && <p className="small muted">BMI {bmi}</p>}
          <label className="span-all">Allergies<input value={f.allergies} onChange={(e) => set('allergies', e.target.value)} placeholder="Medicine – reaction" /></label>
          <label className="span-all">Other conditions<input value={f.comorbidities} onChange={(e) => set('comorbidities', e.target.value)} /></label>
        </div>
        <div className="check-grid">
          <label className="check"><input type="checkbox" checked={f.diabetic} onChange={(e) => set('diabetic', e.target.checked)} /> Diabetic</label>
          <label className="check"><input type="checkbox" checked={f.sleepApnoea} onChange={(e) => set('sleepApnoea', e.target.checked)} /> Sleep apnoea</label>
          <label className="check"><input type="checkbox" checked={f.anticoagulated} onChange={(e) => set('anticoagulated', e.target.checked)} /> Takes blood thinners</label>
          <label className="check"><input type="checkbox" checked={f.responsibleAdultArranged} onChange={(e) => set('responsibleAdultArranged', e.target.checked)} /> Adult to take them home and stay overnight</label>
          <label className="check"><input type="checkbox" checked={f.consentSigned} onChange={(e) => set('consentSigned', e.target.checked)} /> Consent signed</label>
        </div>
        {f.anticoagulated && <label>Which blood thinner, and the stopping plan<input value={f.anticoagulantDetails} onChange={(e) => set('anticoagulantDetails', e.target.value)} /></label>}
        {f.consentSigned && <label>Consent signed on<input type="date" value={f.consentSignedOn} onChange={(e) => set('consentSignedOn', e.target.value)} /></label>}
      </fieldset>
      <fieldset className="stack"><legend>Letters and documents</legend>
        <div className="row">
          <label>What is it<select value={fileCat} onChange={(e) => setFileCat(e.target.value)}>{CATEGORIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          {files.length < opts.maxFiles && <label className="btn btn-light file-pick">Attach a PDF or scan…<input type="file" accept="application/pdf,image/jpeg,image/png,image/tiff,.pdf,.jpg,.jpeg,.png,.tif,.tiff" className="sr-only" onChange={(e) => { void addFile(e.target.files?.[0]); e.target.value = ''; }} /></label>}
        </div>
        {files.length > 0 && <ul className="plain">{files.map((p, i) => <li key={i}>{CATEGORIES.find(([k]) => k === p.category)?.[1]}: {p.fileName} <span className="small muted">({Math.round(p.size / 1024)} KB)</span> <button className="btn btn-link" onClick={() => setFiles(files.filter((_, j) => j !== i))}>Remove {p.fileName}</button></li>)}</ul>}
      </fieldset>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {confirm && <div className="warn-box" role="alert"><ul>{confirm.map((w) => <li key={w}>{w}</li>)}</ul><div className="row"><button className="btn btn-primary" disabled={busy} onClick={() => void submit(true)}>Send anyway</button><button className="btn btn-light" onClick={() => setConfirm(null)}>Go back</button></div></div>}
      {!confirm && <div><button className="btn btn-primary" disabled={busy || files.some((p) => p.contentBase64 === '')} onClick={() => void submit(false)}>{busy ? 'Sending…' : 'Send booking request'}</button></div>}
    </div>
  );
}

export interface RequestRow { id: string; number: string; status: string; source: string; submittedAt: string; specialist: string; roomsName?: string; patientName: string; nhi?: string; dateOfBirth: string; patientMatched: boolean; procedure: string; priority: string; funder: string; earliestDate?: string; latestDate?: string; files: number; awaitingRooms: boolean; awaitingTds: boolean; flags: number; waitlistStatus?: string; bookedFor?: string; daysOpen: number }

export function RequestList({ rows, onOpen, office, label }: { rows: RequestRow[]; onOpen: (id: string) => void; office: boolean; label: string }) {
  if (rows.length === 0) return <p className="muted">No booking requests here.</p>;
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label={label}>
      <table className="results">
        <thead><tr><th>Request</th><th>Patient</th><th>Procedure</th><th>Specialist</th><th>Dates wanted</th><th>Status</th></tr></thead>
        <tbody>{rows.map((r) => (
          <tr key={r.id}>
            <td><button className="btn btn-link" onClick={() => onOpen(r.id)}>{r.number}</button><div className="small muted">{nzDateTime(r.submittedAt)}{r.source !== 'Portal' ? ` · ${r.source}` : ''}</div></td>
            <td>{r.patientName}<div className="small muted">{r.nhi ?? 'no NHI'} · born {formatDate(r.dateOfBirth)}{office && !r.patientMatched && r.status !== 'Declined' && r.status !== 'Withdrawn' ? ' · not matched' : ''}</div></td>
            <td>{r.procedure}<div className="small muted">{r.priority} · {r.funder}{r.files ? ` · ${r.files} file${r.files > 1 ? 's' : ''}` : ''}</div></td>
            <td>{r.specialist}{office && r.roomsName && <div className="small muted">{r.roomsName}</div>}</td>
            <td className="small">{r.earliestDate ? formatDate(r.earliestDate) : 'any'} – {r.latestDate ? formatDate(r.latestDate) : 'any'}</td>
            <td><span className={`pill ${STATUS_PILL[r.status] ?? ''}`}>{STATUS_TEXT[r.status] ?? r.status}</span>
              {office && r.flags > 0 && r.status !== 'Accepted' && <div className="small">{r.flags} point{r.flags > 1 ? 's' : ''} to check</div>}
              {r.status === 'Query' && !office && <div className="small"><strong>Please answer</strong></div>}
              {r.status === 'Query' && office && r.awaitingTds && <div className="small"><strong>Rooms replied</strong></div>}
              {r.waitlistStatus && <div className="small">{r.waitlistStatus === 'Booked' && r.bookedFor ? `Booked ${formatDate(r.bookedFor)}` : `Waitlist: ${r.waitlistStatus.toLowerCase()}`}</div>}</td>
          </tr>))}</tbody>
      </table>
    </div>
  );
}

/** One request: details, points to check, files, the conversation, and (booking office) match / accept / decline. */
export function RequestDetail({ id, base, office, onChanged, onClose }: { id: string; base: string; office: boolean; onChanged: () => void; onClose: () => void }) {
  const [r, setR] = useState<any>(null);
  const [text, setText] = useState('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [acc, setAcc] = useState<any>(null);
  const [dupes, setDupes] = useState<any[] | null>(null);
  const [fileCat, setFileCat] = useState('Referral');
  const load = () => api(`${base}/${id}`).then((x) => { setR(x); setAcc({ procedure: x.procedure, laterality: x.laterality, anaesthetic: x.anaesthetic, durationMinutes: String(x.durationMinutes), priority: x.priority, notes: '' }); }).catch((e) => setErrors(errs(e)));
  useEffect(() => { setR(null); setErrors([]); setMsg(null); setDupes(null); void load(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<unknown>, done: string) => {
    setErrors([]); setMsg(null);
    try { await fn(); setMsg(done); setText(''); setReason(''); await load(); onChanged(); } catch (e) { setErrors(errs(e)); }
  };
  const register = async (confirm: boolean) => {
    setErrors([]); setMsg(null);
    try { await api(`${base}/${id}/register`, { body: { confirmNotDuplicate: confirm } }); setDupes(null); setMsg('Patient registered.'); await load(); onChanged(); }
    catch (e) { if (e instanceof ApiError && e.body?.duplicates?.length) setDupes(e.body.duplicates); setErrors(errs(e)); }
  };
  const addFile = async (file: File | undefined) => {
    if (!file) return;
    await act(async () => api(`${base}/${id}/files`, { body: { category: fileCat, fileName: file.name, contentBase64: await fileToBase64(file) } }), 'File added.');
  };
  if (!r) return errors.length ? <section className="card"><div className="error" role="alert">{errors.join(' ')}</div></section> : <section className="card"><p>Loading…</p></section>;
  const open = r.status === 'Submitted' || r.status === 'Query';
  const yes = (b: boolean) => (b ? 'Yes' : 'No');
  return (
    <section className="card stack" aria-label={`Booking request ${r.number}`}>
      <div className="row-between"><h2>{r.number} · {r.familyName.toUpperCase()}, {r.givenNames}</h2><button className="btn btn-light btn-small" onClick={onClose}>Close {r.number}</button></div>
      <p><span className={`pill ${STATUS_PILL[r.status] ?? ''}`}>{STATUS_TEXT[r.status] ?? r.status}</span> Sent {nzDateTime(r.submittedAt)} by {r.submittedBy}{r.source !== 'Portal' ? ` (${r.source} file)` : ''}{r.externalRef ? ` · rooms ref ${r.externalRef}` : ''}</p>
      {r.declineReason && <p className="warn-box">{r.status === 'Declined' ? 'Declined' : 'Withdrawn'}: {r.declineReason}</p>}
      {r.waitlist && <p className="notice" role="status">On the waitlist ({r.waitlist.priority}, target {formatDate(r.waitlist.targetDate)}) – {r.waitlist.status === 'Booked' && r.waitlist.bookedFor ? `booked for ${formatDate(r.waitlist.bookedFor)}` : r.waitlist.status.toLowerCase()}.
        {office && <> <Link to="/waitlist">Open the waitlist to book a date</Link></>}</p>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {r.flags.length > 0 && <div className="warn-box"><strong>Points to check</strong><ul>{r.flags.map((x: string) => <li key={x}>{x}</li>)}</ul></div>}
      <div className="form-grid">
        <div><h3>Patient</h3><p>NHI {r.nhi ?? '—'} · born {formatDate(r.dateOfBirth)} · {r.sex}<br />{[r.addressLine1, r.suburb, r.city, r.postcode].filter(Boolean).join(', ') || 'No address'}<br />{r.mobilePhone ?? ''} {r.email ?? ''}{r.interpreterLanguage && <><br />Interpreter: {r.interpreterLanguage}</>}</p></div>
        <div><h3>Procedure</h3><p>{r.procedure}{r.laterality !== 'NotApplicable' ? ` (${r.laterality.toLowerCase()})` : ''}<br />{r.anaesthetic} · {r.durationMinutes} min · {r.service} · {r.priority}<br />{r.specialist}</p></div>
        <div><h3>Funding and dates</h3><p>{FUNDERS.find(([k]) => k === r.funder)?.[1] ?? r.funder}{r.insurerName ? ` – ${r.insurerName}` : ''}{r.funderReference ? ` · ${r.funderReference}` : ''}<br />{r.earliestDate ? formatDate(r.earliestDate) : 'any'} – {r.latestDate ? formatDate(r.latestDate) : 'any'}{r.availability && <><br />{r.availability}</>}</p></div>
        <div><h3>Before surgery</h3><p>{r.weightKg ? `${r.weightKg} kg` : ''}{r.heightCm ? ` · ${r.heightCm} cm` : ''}{r.bmi ? ` · BMI ${r.bmi}` : ''}{r.asa ? ` · ASA ${r.asa}` : ''}<br />Diabetic {yes(r.diabetic)} · sleep apnoea {yes(r.sleepApnoea)} · blood thinners {yes(r.anticoagulated)}{r.anticoagulantDetails ? ` (${r.anticoagulantDetails})` : ''}<br />Allergies: {r.allergies ?? 'none sent'}{r.comorbidities && <><br />{r.comorbidities}</>}<br />Adult home overnight {yes(r.responsibleAdultArranged)} · consent {r.consentSigned ? `signed${r.consentSignedOn ? ' ' + formatDate(r.consentSignedOn) : ''}` : 'not signed'}</p></div>
        <div><h3>Rooms</h3><p>{r.roomsName ?? '—'}<br />{r.contactName}{r.contactPhone ? ` · ${r.contactPhone}` : ''}{r.contactEmail && <><br />{r.contactEmail}</>}</p></div>
      </div>
      {r.clinicalNotes && <><h3>Clinical notes</h3><p className="pre-wrap">{r.clinicalNotes}</p></>}
      <h3>Files</h3>
      {r.files.length === 0 ? <p className="muted">None.</p> : <ul className="plain">{r.files.map((x: any) => <li key={x.id}><a href={`${base}/${id}/files/${x.id}`} target="_blank" rel="noopener noreferrer">{x.fileName}</a> <span className="small muted">{x.category} · {Math.round(x.sizeBytes / 1024)} KB{x.filed ? ' · filed in the patient record' : ''}</span></li>)}</ul>}
      {open && <div className="row"><label>What is it<select value={fileCat} onChange={(e) => setFileCat(e.target.value)}>{CATEGORIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label className="btn btn-light file-pick">Add a file…<input type="file" accept="application/pdf,image/jpeg,image/png,image/tiff,.pdf,.jpg,.jpeg,.png,.tif,.tiff" className="sr-only" onChange={(e) => { void addFile(e.target.files?.[0]); e.target.value = ''; }} /></label></div>}

      <h3>Messages</h3>
      {r.messages.length === 0 ? <p className="muted">No messages.</p> : <ul className="plain stack">{r.messages.map((m: any) => <li key={m.id} className={m.fromTds ? 'msg-tds' : 'msg-rooms'}><strong>{m.fromTds ? (office ? `TDS – ${m.author}` : m.author) : `Rooms – ${m.author}`}</strong> <span className="small muted">{nzDateTime(m.at)}</span><div className="pre-wrap">{m.text}</div></li>)}</ul>}
      {open && <div className="stack"><label>{office ? 'Question for the rooms' : 'Message to the TDS booking office'}<textarea rows={2} value={text} onChange={(e) => setText(e.target.value)} /></label>
        <div><button className="btn btn-light" onClick={() => void act(() => api(`${base}/${id}/messages`, { body: { text } }), 'Message sent.')}>{office ? 'Ask the rooms' : 'Send message'}</button></div></div>}

      {office && open && <>
        <h3>Patient record</h3>
        {r.patient ? <p className="notice" role="status">Matched to <Link to={`/patients/${r.patient.id}`}>{r.patient.familyName.toUpperCase()}, {r.patient.givenNames}</Link> ({r.patient.mrn}{r.patient.nhi ? `, NHI ${r.patient.nhi}` : ''}).</p> : <>
          {r.matches?.length > 0 ? <div className="table-scroll" tabIndex={0} role="region" aria-label="Possible patient matches"><table className="results compact"><thead><tr><th>Registered patient</th><th>NHI</th><th>Born</th><th>Why</th><th>Use</th></tr></thead>
            <tbody>{r.matches.map((m: any) => <tr key={m.id}><td>{m.familyName.toUpperCase()}, {m.givenNames} <span className="small muted">{m.mrn}</span></td><td>{m.nhi ?? '—'}</td><td>{formatDate(m.dateOfBirth)}</td><td className="small">{m.reason}</td>
              <td><button className="btn btn-light btn-small" onClick={() => void act(() => api(`${base}/${id}/match`, { body: { patientId: m.id } }), 'Patient matched.')}>Use {m.mrn}</button></td></tr>)}</tbody></table></div>
            : <p className="muted">No registered patient matches these details.</p>}
          <div className="row"><button className="btn btn-light" onClick={() => void register(false)}>Register as a new patient</button>
            {dupes && <button className="btn btn-light" onClick={() => void register(true)}>They are a different person – register</button>}</div>
        </>}
        <h3>Decision</h3>
        {acc && <div className="form-grid">
          <label className="span-all">Procedure for the waitlist<input value={acc.procedure} onChange={(e) => setAcc({ ...acc, procedure: e.target.value })} /></label>
          <label>Side<select value={acc.laterality} onChange={(e) => setAcc({ ...acc, laterality: e.target.value })}><option value="NotApplicable">Not applicable</option><option value="Left">Left</option><option value="Right">Right</option><option value="Bilateral">Both</option></select></label>
          <label>Anaesthetic<select value={acc.anaesthetic} onChange={(e) => setAcc({ ...acc, anaesthetic: e.target.value })}>{['General', 'Sedation', 'Regional', 'Local', 'None'].map((a) => <option key={a} value={a}>{a}</option>)}</select></label>
          <label>Minutes<input value={acc.durationMinutes} onChange={(e) => setAcc({ ...acc, durationMinutes: e.target.value })} inputMode="numeric" /></label>
          <label>Urgency<select value={acc.priority} onChange={(e) => setAcc({ ...acc, priority: e.target.value })}><option value="Routine">Routine</option><option value="SemiUrgent">Semi-urgent</option><option value="Urgent">Urgent</option></select></label>
          <label className="span-all">Note for the waitlist<input value={acc.notes} onChange={(e) => setAcc({ ...acc, notes: e.target.value })} /></label>
        </div>}
        <div className="row"><button className="btn btn-primary" disabled={!r.patientMatched} title={r.patientMatched ? undefined : 'Match or register the patient first'}
          onClick={() => void act(() => api(`${base}/${id}/accept`, { body: { ...acc, durationMinutes: num(acc.durationMinutes) ?? 0 } }), 'Accepted onto the waitlist. The rooms can see this in the portal.')}>Accept onto the waitlist</button></div>
        <div className="row"><label>Reason for declining<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <button className="btn btn-light" onClick={() => void act(() => api(`${base}/${id}/decline`, { body: { reason } }), 'Declined. The rooms can see the reason in the portal.')}>Decline the request</button></div>
      </>}
      {!office && open && <div className="row"><label>Reason for withdrawing<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        <button className="btn btn-light" onClick={() => void act(() => api(`${base}/${id}/withdraw`, { body: { reason } }), 'Request withdrawn.')}>Withdraw the request</button></div>}
    </section>
  );
}
