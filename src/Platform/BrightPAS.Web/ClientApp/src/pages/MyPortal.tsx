import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { fileToBase64, formatDate } from '../api';

async function post(path: string, body: unknown) {
  const res = await fetch(path, { method: 'POST', credentials: 'omit', headers: { 'X-BrightPAS-Request': '1', 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const t = await res.text(); const data = t ? JSON.parse(t) : null;
  if (!res.ok) throw new Error(data?.errors?.join(' ') ?? (res.status === 404 ? 'This link is not valid. Please phone Tasman Day Surgery.' : res.status === 429 ? 'Too many tries – wait a minute.' : 'Something went wrong. Please try again.'));
  return data;
}

/** Patient portal (no account): the secret link plus the patient's date of birth. Nothing is kept in the browser. */
export default function MyPortal() {
  const { token } = useParams();
  const [dob, setDob] = useState('');
  const [d, setD] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [req, setReq] = useState({ kind: 'Question', bookingId: '', message: '' });
  const [busy, setBusy] = useState(false);
  const nav = useNavigate();
  const money = (n: number) => n.toLocaleString('en-NZ', { style: 'currency', currency: 'NZD' });
  const questionnaire = async () => { setError(null); try { const r = await post(`${base}/questionnaire`, { dateOfBirth: dob }); nav(r.path); } catch (x: any) { setError(x.message); } };
  const accept = async (id: string) => {
    setError(null); setMsg(null);
    try { await post(`${base}/estimates/${id}/accept`, { dateOfBirth: dob }); setMsg('Thank you – we have your acceptance of the estimate.'); await open(); } catch (x: any) { setError(x.message); }
  };
  const base = `/api/my/${encodeURIComponent(token ?? '')}`;
  const open = async (e?: React.FormEvent) => { e?.preventDefault(); setError(null); try { setD(await post(`${base}/open`, { dateOfBirth: dob })); } catch (x: any) { setError(x.message); } };
  const send = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null); setMsg(null);
    try { await post(`${base}/requests`, { dateOfBirth: dob, kind: req.kind, bookingId: req.bookingId || null, message: req.message }); setReq({ kind: 'Question', bookingId: '', message: '' }); setMsg('Thank you – we will get back to you.'); await open(); }
    catch (x: any) { setError(x.message); }
  };
  const upload = async (f: File | undefined) => {
    if (!f) return; setError(null); setMsg(null); setBusy(true);
    try { await post(`${base}/documents`, { dateOfBirth: dob, fileName: f.name, contentBase64: await fileToBase64(f), title: f.name }); setMsg(`${f.name} uploaded – thank you.`); }
    catch (x: any) { setError(x.message); } finally { setBusy(false); }
  };
  return (
    <div className="portal-page stack-lg">
      <header><span className="brand-mark">BrightPAS</span> <strong>Tasman Day Surgery</strong></header>
      {!d ? (
        <form className="card stack" onSubmit={open}>
          <h1>Your appointments</h1>
          <p>To keep your information safe, please enter your date of birth.</p>
          <label>Date of birth<input type="date" value={dob} onChange={(e) => setDob(e.target.value)} required /></label>
          {error && <p className="error" role="alert">{error}</p>}
          <button className="btn btn-primary btn-big">Continue</button>
        </form>
      ) : (<>
        <h1>Kia ora {d.givenName}</h1>
        <section className="card stack"><h2>Your procedure</h2>
          {d.procedures.length === 0 ? <p className="muted">No procedure booked.</p> : d.procedures.map((p: any) => <div key={p.id}><strong>{formatDate(p.date)}</strong> – {p.procedure} with {p.surgeon} ({p.location}){!p.confirmed && ' – to be confirmed'}<div className="small muted">We will tell you your arrival time and fasting instructions before the day.</div></div>)}
        </section>
        {d.questionnaire && d.questionnaire !== 'none' && <section className="card stack"><h2>Pre-admission health questionnaire</h2>
          {d.questionnaire === 'submitted' ? <p>We have your questionnaire – thank you. If anything about your health changes before the day, please phone us.</p> : <>
            <p>{d.questionnaire === 'started' ? 'You have started your questionnaire. Please finish and send it.' : 'Please fill in your health questionnaire before your procedure. It takes about 10 minutes.'}</p>
            <div><button className="btn btn-primary" onClick={questionnaire}>{d.questionnaire === 'started' ? 'Continue questionnaire' : 'Start questionnaire'}</button></div></>}
        </section>}
        {d.estimate && <section className="card stack"><h2>Your estimate {d.estimate.number}</h2>
          <dl className="estimate-lines">{d.estimate.lines.map((l: any, i: number) => <div key={i}><dt>{l.description}</dt><dd>{money(l.amount)}</dd></div>)}
            <div><dt>GST</dt><dd>{money(d.estimate.gst)}</dd></div><div className="total"><dt>Total (incl. GST)</dt><dd>{money(d.estimate.total)}</dd></div></dl>
          <p className="small muted">{d.estimate.payerName ? `To be paid by ${d.estimate.payerName}. ` : ''}{d.estimate.validUntil ? `Valid until ${formatDate(d.estimate.validUntil)}. ` : ''}This is an estimate; the final account may differ if your care changes.</p>
          {d.estimate.status === 'Accepted' ? <p className="ok-text">You accepted this estimate.</p> : <div><button className="btn btn-primary" onClick={() => accept(d.estimate.id)}>I accept this estimate</button></div>}
        </section>}
        <section className="card stack"><h2>Clinic appointments</h2>
          {d.appointments.length === 0 ? <p className="muted">None booked.</p> : d.appointments.map((a: any) => <div key={a.id}>{formatDate(a.date)} {a.startTime.slice(0, 5)} – {a.kind === 'PhoneCall' ? 'phone call' : a.kind.toLowerCase()} with {a.clinician}</div>)}
        </section>
        <form className="card stack" onSubmit={send}><h2>Ask us or request a change</h2>
          <label>What do you need?<select value={req.kind} onChange={(e) => setReq({ ...req, kind: e.target.value })}><option value="Question">A question</option><option value="ChangeAppointment">Change my appointment</option><option value="Cancel">Cancel my appointment</option><option value="UpdateDetails">Update my contact details</option></select></label>
          {d.procedures.length > 0 && (req.kind === 'ChangeAppointment' || req.kind === 'Cancel') && <label>Which booking?<select value={req.bookingId} onChange={(e) => setReq({ ...req, bookingId: e.target.value })}><option value="">—</option>{d.procedures.map((p: any) => <option key={p.id} value={p.id}>{formatDate(p.date)} {p.procedure}</option>)}</select></label>}
          <label>Message<textarea rows={4} value={req.message} onChange={(e) => setReq({ ...req, message: e.target.value })} required maxLength={1000} /></label>
          <p className="hint">On file: mobile {d.contact.mobile ?? '—'}, email {d.contact.email ?? '—'}. Urgent or unwell? Phone us or call 111.</p>
          <button className="btn btn-primary">Send</button>
        </form>
        <section className="card stack"><h2>Send us a document</h2>
          <p>For example a referral, insurance approval or a photo of a form (PDF or photo, up to 10 MB).</p>
          <label className="btn btn-light file-btn">Choose file or take a photo<input type="file" className="sr-only" accept="application/pdf,image/jpeg,image/png" disabled={busy} onChange={(e) => { upload(e.target.files?.[0]); e.target.value = ''; }} /></label>
        </section>
        {d.requests.length > 0 && <section className="card stack"><h2>Your messages</h2>{d.requests.map((r: any) => <div key={r.id}><div className="small muted">{new Date(r.createdAt).toLocaleDateString('en-NZ')} – {r.status === 'Open' ? 'waiting for reply' : r.status.toLowerCase()}</div><div>{r.message}</div>{r.response && <div><strong>TDS:</strong> {r.response}</div>}</div>)}</section>}
        {msg && <p className="ok-text" role="status">{msg}</p>}
        {error && <p className="error" role="alert">{error}</p>}
        <button className="btn btn-light" onClick={() => { setD(null); setDob(''); }}>Finish</button>
      </>)}
    </div>
  );
}
