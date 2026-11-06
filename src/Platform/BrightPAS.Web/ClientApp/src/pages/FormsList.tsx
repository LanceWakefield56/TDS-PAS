import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { api, errText, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

export interface Flag { level: string; text: string }
export interface FormRow {
  id: string; patientId: string; patientName: string; nhi: string | null; mrn: string; status: string; createdAt: string; createdBy: string; expiresAt: string;
  submittedAt: string | null; suggested: string | null; category: string | null; triagedBy: string | null; flags: Flag[];
}
export const catClass = (c: string | null) => c === 'Red' ? 'red' : c === 'Amber' ? 'amber' : c === 'Green' ? 'green' : '';
export const catText = (c: string | null) => c === 'Red' ? 'Red – anaesthetist review' : c === 'Amber' ? 'Amber – nurse call' : c === 'Green' ? 'Green – no call needed' : '—';

export default function FormsList() {
  const { can } = useSession();
  const navigate = useNavigate();
  const [view, setView] = useState(can(Perm.FormsTriage) ? 'to-triage' : 'waiting');
  const [rows, setRows] = useState<FormRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRows(null); api<FormRow[]>(`/api/forms?view=${view}`).then(setRows).catch((e) => setError(errText(e))); }, [view]);
  const open = (r: FormRow) => can(Perm.FormsTriage) && r.submittedAt ? navigate(`/forms/${r.id}`) : navigate(`/patients/${r.patientId}`);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Pre-admission forms</h1><p className="muted">Patients complete their health questionnaire online; nurses triage each one. Send a form from the patient's record.</p></div>
      <section className="card">
        <div className="tabs">{[['to-triage', 'To triage'], ['waiting', 'Waiting for patient'], ['triaged', 'Triaged']].map(([k, t]) =>
          <button key={k} className={view === k ? 'on' : ''} onClick={() => setView(k)}>{t}</button>)}</div>
        {error && <p className="error">{error}</p>}
        {rows?.length === 0 && <p className="muted">Nothing here.</p>}
        {rows && rows.length > 0 && (
          <table className="results">
            <thead><tr><th>Patient</th><th>{view === 'waiting' ? 'Sent' : 'Submitted'}</th><th>Suggested</th><th>Triage</th><th>Flags</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id} tabIndex={0} onClick={() => open(r)} onKeyDown={(e) => e.key === 'Enter' && open(r)}>
                <td data-label="Patient"><strong>{r.patientName}</strong><div className="muted small">{r.nhi ?? '—'} · {r.mrn}</div></td>
                <td data-label={view === 'waiting' ? 'Sent' : 'Submitted'}>{nzDateTime(view === 'waiting' ? r.createdAt : r.submittedAt)}{view === 'waiting' && <div className="muted small">Expires {nzDateTime(r.expiresAt)} · {r.status}</div>}</td>
                <td data-label="Suggested">{r.suggested ? <span className={`pill ${catClass(r.suggested)}`}>{r.suggested}</span> : '—'}</td>
                <td data-label="Triage">{r.category ? <><span className={`pill ${catClass(r.category)}`}>{r.category}</span><div className="muted small">{r.triagedBy}</div></> : '—'}</td>
                <td data-label="Flags">{r.flags.filter((f) => f.level !== 'Info').slice(0, 4).map((f) => <div key={f.text} className="small"><span className={`pill ${f.level === 'Red' ? 'red' : 'amber'}`}>{f.level}</span> {f.text}</div>)}</td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}
