import { useEffect, useState } from 'react';
import { useParams } from 'react-router';
import { api, errText } from '../api';

interface Q { id: string; text: string; type: string; required: boolean; options: string[] | null; detailsPrompt: string | null; help: string | null }
interface S { title: string; intro: string | null; questions: Q[] }

/** Patient-facing page opened from the link we send. No account needed: the link plus date of birth. */
export default function PatientQuestionnaire() {
  const { token } = useParams();
  const [stage, setStage] = useState<'loading' | 'dob' | 'form' | 'done' | 'closed' | 'submitted'>('loading');
  const [dob, setDob] = useState('');
  const [name, setName] = useState('');
  const [sections, setSections] = useState<S[]>([]);
  const [a, setA] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    api<{ status: string }>(`/api/q/${token}`).then((r) => setStage(r.status === 'open' ? 'dob' : r.status === 'submitted' ? 'submitted' : 'closed')).catch(() => setStage('closed'));
  }, [token]);

  const fail = (e: unknown) => setErrors(((e as any)?.body?.errors as string[]) ?? [errText(e)]);
  const open = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    try { const r = await api<{ givenName: string; sections: S[]; answers: Record<string, string> }>(`/api/q/${token}/open`, { body: { dateOfBirth: dob } }); setName(r.givenName); setSections(r.sections); setA(r.answers); setStage('form'); }
    catch (x) { fail(x); }
  };
  const save = async (final: boolean) => {
    setErrors([]); setSaved(false);
    try { await api(`/api/q/${token}/${final ? 'submit' : 'save'}`, { body: { dateOfBirth: dob, answers: a } }); if (final) setStage('done'); else setSaved(true); }
    catch (x) { fail(x); window.scrollTo(0, 0); }
  };
  const set = (k: string, v: string) => setA({ ...a, [k]: v });

  return (
    <div className="q-page">
      <header className="q-head"><strong>Tasman Day Surgery</strong> · Pre-admission health questionnaire</header>
      <main className="q-body stack-lg">
        {stage === 'loading' && <p>Loading…</p>}
        {stage === 'closed' && <section className="card"><h1>This link is no longer active</h1><p>It may have expired or been replaced. Please phone Tasman Day Surgery.</p></section>}
        {stage === 'submitted' && <section className="card"><h1>Thank you</h1><p>We have received your questionnaire. A nurse will contact you if we need anything else.</p></section>}
        {stage === 'done' && <section className="card"><h1>Thank you{name ? `, ${name}` : ''}</h1><p>Your answers have been sent securely to our nurses. We will phone you if we need to talk about anything before your procedure.</p></section>}
        {stage === 'dob' && (
          <form className="card stack" onSubmit={open}>
            <h1>Welcome</h1>
            <p>Before your procedure, please tell us about your health. To keep your information private, first enter your date of birth.</p>
            <label>Date of birth<input type="date" value={dob} onChange={(e) => setDob(e.target.value)} required /></label>
            {errors.map((x) => <p key={x} className="error">{x}</p>)}
            <div><button className="btn btn-primary btn-big">Start</button></div>
          </form>
        )}
        {stage === 'form' && (
          <>
            <section className="card"><h1>Kia ora {name}</h1><p>Please answer every question. You can save and come back later using the same link. If you are unsure, answer as best you can – a nurse will check.</p></section>
            {errors.length > 0 && <div className="error" role="alert"><strong>Please check:</strong><ul>{errors.map((x) => <li key={x}>{x}</li>)}</ul></div>}
            {sections.map((s) => (
              <section key={s.title} className="card">
                <h2>{s.title}</h2>
                {s.intro && <p className="muted">{s.intro}</p>}
                {s.questions.map((q) => (
                  <div key={q.id} className="q-question">
                    {q.type === 'confirm' ? (
                      <label className="check"><input type="checkbox" checked={a[q.id] === 'Yes'} onChange={(e) => set(q.id, e.target.checked ? 'Yes' : '')} /> {q.text}</label>
                    ) : (
                      <>
                        <p id={`q-${q.id}`}>{q.text}{q.required && <span aria-hidden="true"> *</span>}</p>
                        {q.help && <p className="hint">{q.help}</p>}
                        {(q.type === 'yesno' || q.type === 'choice') && (
                          <div className="yesno" role="radiogroup" aria-labelledby={`q-${q.id}`}>
                            {(q.type === 'yesno' ? ['Yes', 'No'] : q.options!).map((o) => <button key={o} type="button" role="radio" aria-checked={a[q.id] === o} className={`chip ${a[q.id] === o ? 'on' : ''}`} onClick={() => set(q.id, o)}>{o}</button>)}
                          </div>
                        )}
                        {q.type === 'number' && <input type="number" inputMode="decimal" aria-labelledby={`q-${q.id}`} value={a[q.id] ?? ''} onChange={(e) => set(q.id, e.target.value)} />}
                        {q.type === 'text' && <input aria-labelledby={`q-${q.id}`} value={a[q.id] ?? ''} onChange={(e) => set(q.id, e.target.value)} />}
                        {q.type === 'multiline' && <textarea aria-labelledby={`q-${q.id}`} value={a[q.id] ?? ''} onChange={(e) => set(q.id, e.target.value)} />}
                        {q.detailsPrompt && a[q.id] === 'Yes' && <label className="q-details">{q.detailsPrompt}<input value={a[q.id + '_details'] ?? ''} onChange={(e) => set(q.id + '_details', e.target.value)} /></label>}
                      </>
                    )}
                  </div>
                ))}
              </section>
            ))}
            {saved && <p className="notice">Saved. You can close this page and come back later with the same link.</p>}
            <div className="row"><button className="btn btn-light btn-big" onClick={() => save(false)}>Save and finish later</button><button className="btn btn-primary btn-big" onClick={() => save(true)}>Send my answers</button></div>
          </>
        )}
      </main>
    </div>
  );
}
