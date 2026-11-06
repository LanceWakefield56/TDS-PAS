import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import { api, errText, nzDateTime } from '../api';
import { useSession } from '../App';
import { catClass, catText, Flag, FormRow } from './FormsList';

interface Q { id: string; text: string; type: string; detailsPrompt: string | null }
interface Detail { row: FormRow; answers: Record<string, string>; triageOutcome: string | null; triageNotes: string | null; triagedAt: string | null; sections: { title: string; questions: Q[] }[] }
const OUTCOMES = ['No call needed – cleared', 'Phoned patient – cleared', 'Phoned patient – instructions given', 'Anaesthetist review requested', 'Surgeon informed', 'Booking deferred'];

export default function FormTriage() {
  const { id } = useParams();
  const { writable } = useSession();
  const [d, setD] = useState<Detail | null>(null);
  const [cat, setCat] = useState('');
  const [outcome, setOutcome] = useState(OUTCOMES[0]);
  const [notes, setNotes] = useState('');
  const [alert, setAlert] = useState(true);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = async () => { try { const x = await api<Detail>(`/api/forms/${id}`); setD(x); setCat(x.row.category ?? x.row.suggested ?? 'Green'); } catch (e) { setError(errText(e)); } };
  useEffect(() => { load(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  const save = async () => {
    setError(null);
    try { await api(`/api/forms/${id}/triage`, { body: { category: cat, outcome, notes, addAllergyAlert: alert } }); setMsg('Triage saved.'); await load(); } catch (e) { setError(errText(e)); }
  };
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const r = d.row;
  const hasAllergy = d.answers.allergies === 'Yes' || d.answers.latex === 'Yes';
  return (
    <div className="stack-lg">
      <p className="no-print"><Link to="/forms">← Pre-admission forms</Link></p>
      <section className="banner">
        <h1>Pre-admission questionnaire</h1>
        <div className="banner-facts"><span><b>Patient</b> <Link className="on-dark" to={`/patients/${r.patientId}`}>{r.patientName}</Link></span><span><b>NHI</b> {r.nhi ?? '—'}</span><span><b>Submitted</b> {nzDateTime(r.submittedAt)}</span></div>
      </section>
      {msg && <p className="notice">{msg}</p>}
      {error && <p className="error">{error}</p>}
      <div className="grid">
        <section className="card action">
          <h2>Flags <span className={`pill ${catClass(r.suggested)}`}>Suggested: {catText(r.suggested)}</span></h2>
          {r.flags.length === 0 ? <p className="muted">No flags.</p> : <ul className="plain">{r.flags.map((f: Flag) => <li key={f.text}><span className={`pill ${f.level === 'Red' ? 'red' : f.level === 'Amber' ? 'amber' : ''}`}>{f.level}</span> {f.text}</li>)}</ul>}
        </section>
        <section className="card">
          <h2>Triage</h2>
          {r.category && <p>Triaged <span className={`pill ${catClass(r.category)}`}>{catText(r.category)}</span> by {r.triagedBy} on {nzDateTime(d.triagedAt)}: {d.triageOutcome}{d.triageNotes && <> – {d.triageNotes}</>}</p>}
          {writable('forms') && (
            <div className="stack">
              <div className="chips">{['Green', 'Amber', 'Red'].map((c) => <button key={c} type="button" className={`chip ${cat === c ? 'on' : ''}`} onClick={() => setCat(c)}>{catText(c)}</button>)}</div>
              <label>Outcome<select value={outcome} onChange={(e) => setOutcome(e.target.value)}>{OUTCOMES.map((o) => <option key={o}>{o}</option>)}</select></label>
              <label>Notes<textarea value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
              {hasAllergy && <label className="check"><input type="checkbox" checked={alert} onChange={(e) => setAlert(e.target.checked)} /> Add the reported allergy as an alert on the patient's record</label>}
              <div><button className="btn btn-primary" onClick={save}>{r.category ? 'Update triage' : 'Save triage'}</button></div>
            </div>
          )}
        </section>
      </div>
      {d.sections.map((s) => (
        <section key={s.title} className="card">
          <h2>{s.title}</h2>
          <dl className="answers">{s.questions.map((q) => {
            const v = d.answers[q.id];
            return (<div key={q.id}><dt>{q.text}</dt><dd className={q.type === 'yesno' && v === 'Yes' ? 'yes' : ''}>{v || '—'}{d.answers[q.id + '_details'] && <> – {d.answers[q.id + '_details']}</>}</dd></div>);
          })}</dl>
        </section>
      ))}
    </div>
  );
}
