import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, formatDate } from '../api';
import { useSession } from '../App';

export interface JAction { label: string; to: string; permission: string }
interface JStage { key: string; label: string; state: 'Done' | 'Now' | 'Waiting' | 'Blocked' | 'Later' | 'Skipped'; detail: string | null; when: string | null; action: JAction | null }
interface JCase { id: string; title: string; surgeryDate: string | null; status: string; stages: JStage[]; next: JStage | null; blockers: string[] }
interface JView { patientId: string; patient: string; nhi: string | null; cases: JCase[]; starts: JAction[] }

const ICON: Record<JStage['state'], string> = { Done: '✓', Now: '●', Waiting: '…', Blocked: '!', Later: '', Skipped: '–' };
const SAY: Record<JStage['state'], string> = { Done: 'done', Now: 'to do now', Waiting: 'waiting', Blocked: 'stopped', Later: 'later', Skipped: 'not needed' };

/** Opens an action: a screen in the app, or a panel further down the patient page. */
export function ActionLink({ a, primary, big }: { a: JAction; primary?: boolean; big?: boolean }) {
  const cls = `btn ${primary ? 'btn-primary' : 'btn-light'}${big ? ' btn-big' : ''}`;
  const hash = a.to.indexOf('#');
  if (hash >= 0 && a.to.startsWith(window.location.pathname))
    return <a className={cls} href={a.to.slice(hash)} onClick={(e) => { e.preventDefault(); document.getElementById(a.to.slice(hash + 1))?.scrollIntoView({ behavior: 'smooth', block: 'start' }); }}>{a.label}</a>;
  return <Link className={cls} to={a.to}>{a.label}</Link>;
}

function StageBar({ c }: { c: JCase }) {
  const shown = c.stages.filter((s) => s.state !== 'Skipped');
  return (
    <ol className="journey" aria-label="Patient journey">
      {shown.map((s) => (
        <li key={s.key} className={`stage ${s.state.toLowerCase()}`} title={s.detail ?? undefined}>
          <span className="dot" aria-hidden="true">{ICON[s.state]}</span>
          <span className="label">{s.label}</span>
          <span className="sr-only"> – {SAY[s.state]}{s.detail ? `: ${s.detail}` : ''}</span>
        </li>
      ))}
    </ol>
  );
}

function CaseView({ c, can, open }: { c: JCase; can: (p: string) => boolean; open: boolean }) {
  const [details, setDetails] = useState(false);
  const next = c.next;
  const mayDo = next?.action && can(next.action.permission);
  return (
    <div className="stack">
      <div className="row-between">
        <div><h3 className="case-title">{c.title}</h3><p className="muted">{c.status}{c.surgeryDate && ` · ${formatDate(c.surgeryDate)}`}</p></div>
        {open && next && (
          <div className="next-step">
            <span className="muted small">Next step</span>
            <strong>{next.label}{next.detail ? ` – ${next.detail}` : ''}</strong>
            {next.action && (mayDo ? <ActionLink a={next.action} primary big /> : <span className="small muted">Someone else does this step ({next.action.label.toLowerCase()}).</span>)}
          </div>
        )}
        {open && !next && <div className="next-step done"><strong>Nothing to do – the journey is complete.</strong></div>}
      </div>
      <StageBar c={c} />
      {c.blockers.length > 0 && <div className="warn-box" role="alert"><strong>Stopped:</strong> {c.blockers.join(' · ')}</div>}
      <button className="btn btn-link" aria-expanded={details} onClick={() => setDetails(!details)}>{details ? 'Hide the steps' : 'Show every step'}</button>
      {details && (
        <table className="results compact">
          <tbody>{c.stages.filter((s) => s.state !== 'Skipped').map((s) => (
            <tr key={s.key}><th scope="row">{s.label}</th><td><span className={`pill ${s.state === 'Done' ? 'green' : s.state === 'Blocked' ? 'red' : s.state === 'Now' ? 'blue' : s.state === 'Waiting' ? 'amber' : 'muted'}`}>{SAY[s.state]}</span></td>
              <td className="small">{s.detail ?? ''}</td><td>{s.action && can(s.action.permission) && <ActionLink a={s.action} />}</td></tr>))}</tbody>
        </table>
      )}
    </div>
  );
}

/** The patient's journey: where they are, what's stopping them, and the one next step. Shown at the top of the patient page. */
export default function JourneyPanel({ patientId }: { patientId: string }) {
  const { can } = useSession();
  const [v, setV] = useState<JView | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => { api<JView>(`/api/journey/patients/${patientId}`).then(setV).catch(() => setFailed(true)); }, [patientId]);
  if (failed) return null;
  if (!v) return <section className="card journey-card"><p className="muted">Loading the journey…</p></section>;
  const [current, ...others] = v.cases;
  const starts = v.starts.filter((a) => can(a.permission));
  return (
    <section className="card journey-card stack" aria-labelledby="journey-h">
      <h2 id="journey-h">Journey</h2>
      {current ? <CaseView c={current} can={can} open /> : <p className="muted">No referral, estimate or booking yet.</p>}
      {others.length > 0 && (
        <details>
          <summary>{others.length} other episode{others.length === 1 ? '' : 's'}</summary>
          <div className="stack">{others.map((c) => <div key={c.id} className="card subtle"><CaseView c={c} can={can} open /></div>)}</div>
        </details>
      )}
      {starts.length > 0 && (
        <div className="row wrap"><span className="muted">Start something new:</span>{starts.map((a) => <ActionLink key={a.to} a={a} />)}</div>
      )}
    </section>
  );
}
