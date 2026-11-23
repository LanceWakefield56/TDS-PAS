import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, ApiError, errText, nzDateTime } from '../api';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const TYPE: Record<string, string> = { Heading: 'Heading / text', Text: 'Short text', LongText: 'Long text', Number: 'Number', Date: 'Date', YesNo: 'Yes / no', Choice: 'Pick one', MultiChoice: 'Pick any', Signature: 'Signature', Calculated: 'Calculated', Nhi: 'NHI' };
const CAT: Record<string, string> = { Clinical: 'Clinical assessment', Consent: 'Consent', Audit: 'Audit / checklist', Cssd: 'CSSD checklist', Admin: 'Admin / HR', PatientQuestionnaire: 'Patient questionnaire' };
const STATUS: Record<string, string> = { Draft: 'Draft', Testing: 'Testing', Published: 'In use', Retired: 'Retired' };

interface Field { key: string; section: string | null; label: string; type: string; required: boolean; min: number | null; max: number | null; showIfKey: string | null; showIfValue: string | null; calculation: string | null; mapsTo: string | null; help: string | null; options: { value: string; score: number | null }[] }
interface Meta { types: string[]; categories: string[]; dataElements: { key: string; label: string }[]; canDesign: boolean; canApprove: boolean; canFill: boolean }

function useMeta() { const [m, setM] = useState<Meta | null>(null); useEffect(() => { api<Meta>('/api/forms/designer/meta').then(setM).catch(() => setM(null)); }, []); return m; }
const readFile = (f: File, asText: boolean) => new Promise<string>((res, rej) => { const r = new FileReader(); r.onload = () => res(asText ? String(r.result) : String(r.result).split(',')[1]); r.onerror = rej; if (asText) r.readAsText(f); else r.readAsDataURL(f); });

/** All forms: create, import, open. */
export default function FormDesigner() {
  const meta = useMeta();
  const navigate = useNavigate();
  const [forms, setForms] = useState<any[] | null>(null);
  const [n, setN] = useState({ code: '', name: '', category: 'Audit' });
  const [imp, setImp] = useState<{ kind: string; code: string; name: string; category: string; file: File | null; text: string }>({ kind: 'csv', code: '', name: '', category: 'Audit', file: null, text: '' });
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any[]>('/api/forms/designer/forms').then(setForms).catch((e) => setErrors(errs(e))); }, []);
  const create = async () => { setErrors([]); try { const r = await api<any>('/api/forms/designer/forms', { body: n }); navigate(`/forms/designer/${r.id}`); } catch (e) { setErrors(errs(e)); } };
  const doImport = async () => {
    setErrors([]);
    try {
      const body: any = { code: imp.code, name: imp.name, category: imp.category, kind: imp.kind, text: null, base64: null };
      if (imp.kind === 'pdf') { if (!imp.file) throw new Error('Choose the PDF.'); body.base64 = await readFile(imp.file, false); }
      else body.text = imp.file ? await readFile(imp.file, true) : imp.text;
      const r = await api<any>('/api/forms/designer/import', { body });
      navigate(`/forms/designer/${r.id}`);
    } catch (e) { setErrors(errs(e)); }
  };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Form designer</h1><p className="muted">Every form in one place – clinical assessments, consent, audits, CSSD checklists, admin forms. Answers can flow into the record. Clinical forms are approved by Clinical Governance before use.</p></div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      <section className="card">
        {!forms ? <p>Loading…</p> : forms.length === 0 ? <p className="muted">No forms yet.</p> : (
          <table className="results"><thead><tr><th>Form</th><th>Type</th><th>Versions</th></tr></thead>
            <tbody>{forms.map((f) => <tr key={f.id}><td><strong>{f.name}</strong> <span className="muted small">{f.code}</span></td><td>{CAT[f.category]}</td>
              <td><div className="row wrap">{f.versions.map((v: any) => <Link key={v.id} to={`/forms/designer/${v.id}`} className={`pill ${v.status === 'Published' ? 'green' : v.status === 'Retired' ? 'muted' : 'amber'}`}>v{v.version} {STATUS[v.status]}</Link>)}</div></td></tr>)}</tbody></table>
        )}
      </section>
      {meta?.canDesign && (
        <div className="grid-2">
          <section className="card stack"><h2>New form</h2>
            <div className="form-grid">
              <label>Short code<input value={n.code} onChange={(e) => setN({ ...n, code: e.target.value.toUpperCase() })} /></label>
              <label>Name<input value={n.name} onChange={(e) => setN({ ...n, name: e.target.value })} /></label>
              <label>Type<select value={n.category} onChange={(e) => setN({ ...n, category: e.target.value })}>{meta.categories.map((c) => <option key={c} value={c}>{CAT[c]}</option>)}</select></label>
            </div>
            <div><button className="btn btn-primary" onClick={() => void create()}>Create and design</button></div>
          </section>
          <section className="card stack"><h2>Import a form</h2>
            <p className="hint">Opens as a draft for you to check, map fields and add logic – nothing goes live automatically.</p>
            <div className="form-grid">
              <label>From<select value={imp.kind} onChange={(e) => setImp({ ...imp, kind: e.target.value, file: null })}><option value="csv">Excel/CSV question list</option><option value="fhir">FHIR Questionnaire (JSON)</option><option value="pdf">Fillable PDF</option></select></label>
              <label>Short code<input value={imp.code} onChange={(e) => setImp({ ...imp, code: e.target.value.toUpperCase() })} /></label>
              <label>Name{imp.kind === 'fhir' ? ' (blank = from the file)' : ''}<input value={imp.name} onChange={(e) => setImp({ ...imp, name: e.target.value })} /></label>
              <label>Type<select value={imp.category} onChange={(e) => setImp({ ...imp, category: e.target.value })}>{meta.categories.map((c) => <option key={c} value={c}>{CAT[c]}</option>)}</select></label>
              <label>File<input type="file" accept={imp.kind === 'pdf' ? '.pdf' : imp.kind === 'fhir' ? '.json' : '.csv,.txt'} onChange={(e) => setImp({ ...imp, file: e.target.files?.[0] ?? null })} /></label>
            </div>
            {imp.kind === 'csv' && !imp.file && <label>…or paste (columns: question, type, options, required, section, help)<textarea value={imp.text} onChange={(e) => setImp({ ...imp, text: e.target.value })} placeholder={'question,type,options,required\nDo you smoke?,yesno,,yes\nPain,choice,None=0|Mild=1|Severe=3,no'} /></label>}
            <div><button className="btn btn-primary" onClick={() => void doImport()}>Import</button></div>
          </section>
        </div>
      )}
    </div>
  );
}

const blank = (): Field => ({ key: '', section: null, label: '', type: 'Text', required: false, min: null, max: null, showIfKey: null, showIfValue: null, calculation: null, mapsTo: null, help: null, options: [] });
const optText = (o: Field['options']) => o.map((x) => (x.score != null ? `${x.value}=${x.score}` : x.value)).join('\n');
const parseOpts = (s: string) => s.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => { const i = l.lastIndexOf('='); const sc = i > 0 ? Number(l.slice(i + 1)) : NaN; return i > 0 && !Number.isNaN(sc) ? { value: l.slice(0, i).trim(), score: sc } : { value: l, score: null }; });

/** Edit a version: fields, logic, mapping; test, publish, retire, new version, FHIR export, preview. */
export function FormVersionEditor() {
  const { id } = useParams();
  const navigate = useNavigate();
  const meta = useMeta();
  const [v, setV] = useState<any>(null);
  const [fields, setFields] = useState<Field[]>([]);
  const [open, setOpen] = useState<number | null>(null);
  const [note, setNote] = useState('');
  const [preview, setPreview] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(async () => { const x = await api<any>(`/api/forms/designer/versions/${id}`); setV(x); setFields(x.fields); }, [id]);
  useEffect(() => { void load().catch((e) => setErrors(errs(e))); setPreview(false); }, [load]);
  if (!v || !meta) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const editable = meta.canDesign && (v.status === 'Draft' || v.status === 'Testing');
  const governed = ['Clinical', 'Consent', 'PatientQuestionnaire'].includes(v.category);
  const run = async (f: () => Promise<any>, ok: string) => { setErrors([]); setMsg(null); try { const r = await f(); setMsg(ok); if (r?.id && r.id !== id) navigate(`/forms/designer/${r.id}`); else await load(); } catch (e) { setErrors(errs(e)); } };
  const set = (i: number, p: Partial<Field>) => setFields(fields.map((f, j) => (j === i ? { ...f, ...p } : f)));
  const move = (i: number, d: number) => { const a = [...fields]; const [x] = a.splice(i, 1); a.splice(i + d, 0, x); setFields(a); setOpen(i + d); };
  const save = () => run(() => api(`/api/forms/designer/versions/${id}`, { method: 'PUT', body: { name: v.name, header: v.header, footer: v.footer, thirdParty: v.thirdParty, licenceNote: v.licenceNote, fields } }), 'Saved.');
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>{v.name} <span className="muted">v{v.version}</span> <span className={`pill ${v.status === 'Published' ? 'green' : v.status === 'Retired' ? 'muted' : 'amber'}`}>{STATUS[v.status]}</span></h1>
          <p className="muted">{v.code} · {CAT[v.category]} · from {v.source}{v.approvedBy ? ` · approved by ${v.approvedBy} ${nzDateTime(v.approvedAt)}: ${v.approvalNote}` : ''}</p></div>
        <div className="row wrap">
          <Link className="btn btn-light" to="/forms/designer">All forms</Link>
          <button className="btn btn-light" onClick={() => setPreview(!preview)}>{preview ? 'Back to editing' : 'Preview'}</button>
          <a className="btn btn-light" href={`/api/forms/designer/versions/${id}/fhir`}>FHIR export</a>
        </div>
      </div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {preview ? <section className="card"><FormFill fields={fields} values={{}} onChange={() => undefined} readOnly={false} /></section> : <>
        <section className="card stack">
          <div className="form-grid">
            <label>Name<input disabled={!editable} value={v.name} onChange={(e) => setV({ ...v, name: e.target.value })} /></label>
            <label>Header (print)<input disabled={!editable} value={v.header ?? ''} onChange={(e) => setV({ ...v, header: e.target.value })} /></label>
            <label>Footer (print)<input disabled={!editable} value={v.footer ?? ''} onChange={(e) => setV({ ...v, footer: e.target.value })} /></label>
          </div>
          <label className="check"><input type="checkbox" disabled={!editable} checked={v.thirdParty} onChange={(e) => setV({ ...v, thirdParty: e.target.checked })} /> Licensed or third-party form (e.g. a licensed outcome measure)</label>
          {v.thirdParty && <label>TDS's licence (who holds it, terms)<input disabled={!editable} value={v.licenceNote ?? ''} onChange={(e) => setV({ ...v, licenceNote: e.target.value })} /></label>}
        </section>
        <section className="card stack">
          <h2>Fields</h2>
          <ol className="field-list">{fields.map((f, i) => (
            <li key={i} className={open === i ? 'open' : ''}>
              <div className="row-between">
                <button className="btn btn-link field-title" onClick={() => setOpen(open === i ? null : i)}>{f.section && <span className="muted small">{f.section} › </span>}{f.label || '(no label)'} <span className="muted small">{TYPE[f.type]}{f.required ? ' · required' : ''}{f.showIfKey ? ` · if ${f.showIfKey} = ${f.showIfValue}` : ''}{f.mapsTo ? ` · → ${f.mapsTo}` : ''}</span></button>
                {editable && <div className="row"><button className="btn btn-link btn-small" aria-label="Move up" disabled={i === 0} onClick={() => move(i, -1)}>↑</button><button className="btn btn-link btn-small" aria-label="Move down" disabled={i === fields.length - 1} onClick={() => move(i, 1)}>↓</button>
                  <button className="btn btn-link danger btn-small" onClick={() => setFields(fields.filter((_, j) => j !== i))}>Remove</button></div>}
              </div>
              {open === i && (
                <div className="form-grid">
                  <label>Label<input disabled={!editable} value={f.label} onChange={(e) => set(i, { label: e.target.value })} /></label>
                  <label>Type<select disabled={!editable} value={f.type} onChange={(e) => set(i, { type: e.target.value })}>{meta.types.map((t) => <option key={t} value={t}>{TYPE[t]}</option>)}</select></label>
                  <label>Section<input disabled={!editable} value={f.section ?? ''} onChange={(e) => set(i, { section: e.target.value || null })} /></label>
                  <label>Key (for logic and reports)<input disabled={!editable} value={f.key} onChange={(e) => set(i, { key: e.target.value })} placeholder="made from the label" /></label>
                  {f.type !== 'Heading' && f.type !== 'Calculated' && <label className="check"><input type="checkbox" disabled={!editable} checked={f.required} onChange={(e) => set(i, { required: e.target.checked })} /> Required</label>}
                  {f.type === 'Number' && <><label>Lowest<input type="number" disabled={!editable} value={f.min ?? ''} onChange={(e) => set(i, { min: e.target.value === '' ? null : Number(e.target.value) })} /></label><label>Highest<input type="number" disabled={!editable} value={f.max ?? ''} onChange={(e) => set(i, { max: e.target.value === '' ? null : Number(e.target.value) })} /></label></>}
                  {(f.type === 'Choice' || f.type === 'MultiChoice') && <label>Options (one per line; add =score for scoring)<textarea disabled={!editable} value={optText(f.options)} onChange={(e) => set(i, { options: parseOpts(e.target.value) })} /></label>}
                  {f.type === 'Calculated' && <label>Calculation – bmi(weight,height), sum(a,b,…), count(a,b,…)<input disabled={!editable} value={f.calculation ?? ''} onChange={(e) => set(i, { calculation: e.target.value })} /></label>}
                  <label>Show only if field<select disabled={!editable} value={f.showIfKey ?? ''} onChange={(e) => set(i, { showIfKey: e.target.value || null })}><option value="">– always show –</option>{fields.filter((x, j) => j !== i && x.key).map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}</select></label>
                  {f.showIfKey && <label>…has the answer<input disabled={!editable} value={f.showIfValue ?? ''} onChange={(e) => set(i, { showIfValue: e.target.value })} placeholder="yes" /></label>}
                  <label>Answer goes into the record as<select disabled={!editable} value={f.mapsTo ?? ''} onChange={(e) => set(i, { mapsTo: e.target.value || null })}><option value="">– nowhere –</option>{meta.dataElements.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}</select></label>
                  <label>Help text<input disabled={!editable} value={f.help ?? ''} onChange={(e) => set(i, { help: e.target.value || null })} /></label>
                </div>
              )}
            </li>))}</ol>
          {editable && <div className="row wrap"><button className="btn btn-light btn-small" onClick={() => { setFields([...fields, blank()]); setOpen(fields.length); }}>Add a field</button><button className="btn btn-primary" onClick={() => void save()}>Save</button></div>}
        </section>
        <section className="card stack">
          <h2>Use</h2>
          <div className="row wrap">
            {v.status === 'Draft' && meta.canDesign && <button className="btn btn-light" onClick={() => void run(async () => { await save(); await api(`/api/forms/designer/versions/${id}/test`, { body: {} }); }, 'In testing – staff can try it; entries are marked as testing.')}>Start testing</button>}
            {(v.status === 'Published' || v.status === 'Retired') && meta.canDesign && <button className="btn btn-light" onClick={() => void run(() => api(`/api/forms/designer/forms/${v.designId}/new-version`, { body: {} }), 'New draft version made.')}>New version</button>}
            {v.status === 'Published' && meta.canDesign && <button className="btn btn-link danger" onClick={() => void run(() => api(`/api/forms/designer/versions/${id}/retire`, { body: {} }), 'Retired – no new entries; completed ones stay readable.')}>Retire</button>}
            {editable && <button className="btn btn-link danger" onClick={() => void run(async () => { await api(`/api/forms/designer/versions/${id}`, { method: 'DELETE' }); navigate('/forms/designer'); }, 'Deleted.')}>Delete draft</button>}
          </div>
          {(v.status === 'Draft' || v.status === 'Testing') && (meta.canDesign || meta.canApprove) && (
            <div className="card subtle stack">
              <h3>Publish</h3>
              {governed ? <p className="hint">A {CAT[v.category].toLowerCase()} is published by someone with Clinical Governance approval. Record the approval.</p> : <p className="hint">Publishing replaces the version in use. Forms already started finish on the version they started.</p>}
              {governed && <label>Clinical Governance approval (who, when, where)<input value={note} onChange={(e) => setNote(e.target.value)} /></label>}
              <div><button className="btn btn-warn" disabled={governed && (!meta.canApprove || !note.trim())} onClick={() => void run(async () => { if (editable) await save(); await api(`/api/forms/designer/versions/${id}/publish`, { body: { note: note || null } }); }, 'Published.')}>Publish v{v.version}</button></div>
            </div>
          )}
        </section>
      </>}
    </div>
  );
}

/** Same calculations as the server (FormLogic.Calculate), so results show while typing; the server's result is what is saved. */
export function calculate(fields: Field[], values: Record<string, string>): Record<string, string> {
  const v = { ...values };
  for (const f of fields.filter((x) => x.type === 'Calculated' && x.calculation)) {
    const m = /^\s*(\w+)\((.*)\)\s*$/.exec(f.calculation!);
    if (!m) continue;
    const fn = m[1].toLowerCase(); const args = m[2].split(',').map((a) => a.trim()).filter(Boolean);
    let r: number | null = null;
    if (fn === 'bmi' && args.length === 2) { const w = Number(v[args[0]]); const h = Number(v[args[1]]); if (w > 0 && h > 0) r = Math.round((w / ((h / 100) * (h / 100))) * 10) / 10; }
    else if (fn === 'sum' || fn === 'count') {
      r = 0;
      for (const a of args) {
        const s = v[a]; if (!s) continue; const src = fields.find((x) => x.key === a);
        if (fn === 'count') { if (s.toLowerCase() === 'yes') r++; continue; }
        if (src?.type === 'Choice') r += src.options.find((o) => o.value === s)?.score ?? 0;
        else if (src?.type === 'MultiChoice') r += s.split('|').reduce((t, x) => t + (src.options.find((o) => o.value === x)?.score ?? 0), 0);
        else if (s.toLowerCase() === 'yes') r++;
        else if (!Number.isNaN(Number(s))) r += Number(s);
      }
    }
    if (r !== null) v[f.key] = String(r); else delete v[f.key];
  }
  return v;
}

/** Renders a form for filling in, with show/hide logic. */
export function FormFill({ fields, values: raw, onChange, readOnly }: { fields: Field[]; values: Record<string, string>; onChange: (k: string, v: string) => void; readOnly: boolean }) {
  const values = calculate(fields, raw);
  let section: string | null = null;
  const visible = (f: Field) => !f.showIfKey || (values[f.showIfKey] ?? '').toLowerCase() === (f.showIfValue ?? '').toLowerCase();
  return (
    <div className="stack form-fill">
      {fields.filter(visible).map((f) => {
        const head = f.section && f.section !== section ? <h3 className="fill-section">{f.section}</h3> : null;
        section = f.section ?? section;
        const v = values[f.key] ?? '';
        const id = `f-${f.key}`;
        let input;
        switch (f.type) {
          case 'Heading': return <div key={f.key}>{head}{f.label !== f.section && <p className="fill-heading"><strong>{f.label}</strong>{f.help && <span className="muted"> {f.help}</span>}</p>}</div>;
          case 'LongText': input = <textarea id={id} disabled={readOnly} value={v} onChange={(e) => onChange(f.key, e.target.value)} />; break;
          case 'Number': input = <input id={id} type="number" disabled={readOnly} value={v} min={f.min ?? undefined} max={f.max ?? undefined} onChange={(e) => onChange(f.key, e.target.value)} />; break;
          case 'Date': input = <input id={id} type="date" disabled={readOnly} value={v} onChange={(e) => onChange(f.key, e.target.value)} />; break;
          case 'YesNo': input = <div className="row" role="radiogroup" aria-labelledby={`${id}-l`}>{['yes', 'no'].map((x) => <label key={x} className="check"><input type="radio" name={id} disabled={readOnly} checked={v.toLowerCase() === x} onChange={() => onChange(f.key, x)} /> {x === 'yes' ? 'Yes' : 'No'}</label>)}</div>; break;
          case 'Choice': input = <select id={id} disabled={readOnly} value={v} onChange={(e) => onChange(f.key, e.target.value)}><option value="">–</option>{f.options.map((o) => <option key={o.value}>{o.value}</option>)}</select>; break;
          case 'MultiChoice': { const set = v ? v.split('|') : []; input = <div className="row wrap" role="group" aria-labelledby={`${id}-l`}>{f.options.map((o) => <label key={o.value} className="check"><input type="checkbox" disabled={readOnly} checked={set.includes(o.value)} onChange={() => onChange(f.key, (set.includes(o.value) ? set.filter((x) => x !== o.value) : [...set, o.value]).join('|'))} /> {o.value}</label>)}</div>; break; }
          case 'Calculated': input = <output id={id} className="calc">{v || '—'}</output>; break;
          case 'Signature': input = <input id={id} disabled={readOnly} value={v} placeholder="Type full name to sign" onChange={(e) => onChange(f.key, e.target.value)} />; break;
          default: input = <input id={id} disabled={readOnly} value={v} onChange={(e) => onChange(f.key, e.target.value)} />;
        }
        return (
          <div key={f.key}>{head}
            <div className="fill-q"><label id={`${id}-l`} htmlFor={id}>{f.label}{f.required && <span className="req" aria-label="required"> *</span>}{f.help && <span className="small muted"> – {f.help}</span>}</label>{input}</div>
          </div>
        );
      })}
    </div>
  );
}

/** Fill in (or read) one entry; Save keeps a draft, Complete validates and sends mapped answers to the record. */
export function FormEntryPage() {
  const { id } = useParams();
  const meta = useMeta();
  const [e, setE] = useState<any>(null);
  const [v, setV] = useState<any>(null);
  const [vals, setVals] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(async () => { const x = await api<any>(`/api/forms/designer/entries/${id}`); setE(x); setVals(x.values); setV(await api(`/api/forms/designer/versions/${x.versionId}`)); }, [id]);
  useEffect(() => { void load().catch((x) => setErrors(errs(x))); }, [load]);
  if (!e || !v || !meta) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const ro = e.status !== 'InProgress' || !meta.canFill;
  const save = async (complete: boolean) => { setErrors([]); setMsg(null); try { const r = await api<any>(`/api/forms/designer/entries/${id}`, { method: 'PUT', body: { answers: vals, complete } }); setMsg(complete ? `Completed.${r.applied?.length ? ' ' + r.applied.join('; ') + '.' : ''}` : 'Saved – you can finish it later.'); await load(); } catch (x) { setErrors(errs(x)); window.scrollTo(0, 0); } };
  return (
    <div className="stack-lg">
      <div className="page-head row-between no-print">
        <div><h1>{v.name} <span className="muted">v{v.version}</span>{v.status === 'Testing' && <span className="pill amber">testing</span>}</h1>
          <p className="muted">{e.patientId && <Link to={`/patients/${e.patientId}`}>Patient record</Link>} · started by {e.startedBy} {nzDateTime(e.startedAt)}{e.completedAt ? ` · completed by ${e.completedBy} ${nzDateTime(e.completedAt)}` : ''}{e.voidReason ? ` · void: ${e.voidReason}` : ''}</p></div>
        <div className="row"><button className="btn btn-light" onClick={() => window.print()}>Print</button></div>
      </div>
      {v.header && <p className="print-only"><strong>{v.header}</strong></p>}
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((x) => <li key={x}>{x}</li>)}</ul></div>}
      {msg && <p className="notice no-print" role="status">{msg}</p>}
      <section className="card"><FormFill fields={v.fields} values={vals} onChange={(k, x) => setVals({ ...vals, [k]: x })} readOnly={ro} /></section>
      {v.footer && <p className="print-only small">{v.footer}</p>}
      {!ro && <div className="row no-print"><button className="btn btn-light" onClick={() => void save(false)}>Save for later</button><button className="btn btn-primary btn-big" onClick={() => void save(true)}>Complete</button></div>}
    </div>
  );
}

/** Forms panel on the patient record: completed and in-progress forms, and "Fill in a form". */
export function PatientForms({ patientId }: { patientId: string }) {
  const meta = useMeta();
  const navigate = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [forms, setForms] = useState<any[]>([]);
  const [pick, setPick] = useState('');
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<any[]>(`/api/forms/designer/entries?patientId=${patientId}`).then(setRows).catch(() => setRows([])); api<any[]>('/api/forms/designer/forms').then(setForms).catch(() => undefined); }, [patientId]);
  if (!rows || !meta) return null;
  const usable = forms.flatMap((f) => f.versions.filter((v: any) => v.status === 'Published' || v.status === 'Testing').map((v: any) => ({ id: v.id, label: `${f.name}${v.status === 'Testing' ? ' (testing)' : ''}` })));
  if (rows.length === 0 && (!meta.canFill || usable.length === 0)) return null;
  return (
    <section className="card">
      <h2>Forms</h2>
      {error && <p className="error">{error}</p>}
      {rows.length > 0 && <ul className="plain">{rows.slice(0, 10).map((r) => <li key={r.id}><Link to={`/forms/entry/${r.id}`}>{r.name} v{r.version}</Link> · {r.status === 'Completed' ? `completed by ${r.completedBy}` : r.status === 'Voided' ? 'void' : 'in progress'} · {nzDateTime(r.completedAt ?? r.startedAt)}</li>)}</ul>}
      {meta.canFill && usable.length > 0 && <div className="row"><select aria-label="Form to fill in" value={pick} onChange={(e) => setPick(e.target.value)}><option value="">Fill in a form…</option>{usable.map((u) => <option key={u.id} value={u.id}>{u.label}</option>)}</select>
        <button className="btn btn-light btn-small" disabled={!pick} onClick={async () => { try { const r = await api<any>('/api/forms/designer/entries', { body: { versionId: pick, patientId } }); navigate(`/forms/entry/${r.id}`); } catch (x) { setError(errText(x)); } }}>Start</button></div>}
    </section>
  );
}
