import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import { DocNode, DropInput, Errors, errs, FORMAT_LABEL, FORMATS, MsgNav, OP_LABEL, OPERATORS, PATH_HINT, TreeView } from '../components/MsgShared';
import { dicomFileToJson } from './Messaging';

const FUNCTIONS: Record<string, string> = {
  Copy: 'Copy', Constant: 'Fixed value', Translate: 'Translate (table)', Date: 'Date format', Join: 'Join fields', Split: 'Split – take part', Upper: 'UPPER CASE', Lower: 'lower case',
  Default: 'Copy, or default if empty', Substring: 'Part of text', Clear: 'Clear the field',
};
const ARG_HINT: Record<string, string> = { Constant: 'value', Date: 'yyyyMMdd>yyyy-MM-dd', Join: 'separator, e.g. space or ^', Split: 'separator|part, e.g. ^|2', Default: 'default value', Substring: 'start,length e.g. 0,4' };

interface Rule { target: string; source: string | null; function: string; argument: string | null; translationTableId: string | null; conditionSource: string | null; conditionOperator: string | null; conditionValue: string | null }

export function MessagingMappings() {
  const { can } = useSession();
  const nav = useNavigate();
  const [list, setList] = useState<any[] | null>(null);
  const [create, setCreate] = useState<any>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  useEffect(() => { api<any[]>('/api/messaging/mappings').then(setList).catch((e) => setErrors(errs(e))); }, []);
  const add = async () => { setErrors(null); try { const r = await api('/api/messaging/mappings', { body: create }); nav(`/messaging/mappings/${r.id}`); } catch (x) { setErrors(errs(x)); } };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Mappings</h1><p className="muted">Field-by-field translation between message formats (e.g. DICOM → HL7, HL7 → FHIR). Changes are tested on a sample and approved by a second person before going live.</p></div>
      <MsgNav />
      <Errors list={errors} />
      {can(Perm.Integration) && (create ? (
        <section className="card stack side-panel"><h2>New mapping</h2>
          <div className="form-grid">
            <label>Name<input value={create.name} onChange={(e) => setCreate({ ...create, name: e.target.value })} placeholder="e.g. Endoscopy DICOM → HL7 ORU" /></label>
            <label>From<select value={create.sourceFormat} onChange={(e) => setCreate({ ...create, sourceFormat: e.target.value })}>{FORMATS.map((f) => <option key={f} value={f}>{FORMAT_LABEL[f]}</option>)}</select></label>
            <label>To<select value={create.targetFormat} onChange={(e) => setCreate({ ...create, targetFormat: e.target.value })}>{FORMATS.map((f) => <option key={f} value={f}>{FORMAT_LABEL[f]}</option>)}</select></label>
            <label>Description<input value={create.description} onChange={(e) => setCreate({ ...create, description: e.target.value })} /></label>
          </div>
          <div className="row"><button className="btn btn-primary" onClick={add}>Create</button><button className="btn btn-light" onClick={() => setCreate(null)}>Cancel</button></div>
        </section>) : <div><button className="btn btn-primary" onClick={() => setCreate({ name: '', sourceFormat: 'Dicom', targetFormat: 'Hl7v2', description: '' })}>New mapping</button></div>)}
      {list && <table className="results"><thead><tr><th>Mapping</th><th>From → to</th><th>Live</th><th>In progress</th><th>Used in flows</th></tr></thead>
        <tbody>{list.map((m) => <tr key={m.id}><td><Link to={`/messaging/mappings/${m.id}`}><strong>{m.name}</strong></Link><div className="small muted">{m.description}</div></td>
          <td>{FORMAT_LABEL[m.sourceFormat]} → {FORMAT_LABEL[m.targetFormat]}</td><td>{m.live ? `v${m.live}` : <span className="muted">none</span>}</td>
          <td>{m.awaiting ? <span className="badge warn">v{m.awaiting} waiting for approval</span> : m.draft ? <span className="badge">v{m.draft} draft</span> : ''}</td><td>{m.usedInFlows}</td></tr>)}</tbody></table>}
    </div>
  );
}

export function MappingEditor() {
  const { id } = useParams();
  const { can, me } = useSession();
  const canEdit = can(Perm.Integration);
  const [m, setM] = useState<any>(null);
  const [ver, setVer] = useState<any>(null);
  const [rules, setRules] = useState<Rule[]>([]);
  const [filters, setFilters] = useState<any[]>([]);
  const [tables, setTables] = useState<any[]>([]);
  const [catalogue, setCatalogue] = useState<any>(null);
  const [preview, setPreview] = useState<any>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [filter, setFilter] = useState('');
  const [outView, setOutView] = useState<'tree' | 'raw'>('tree');
  const [focus, setFocus] = useState<{ row: number; field: 'source' | 'target' | 'cond' } | null>(null);
  const [reason, setReason] = useState('');
  const timer = useRef<number | undefined>(undefined);

  const load = async (versionId?: string) => {
    const r = await api(`/api/messaging/mappings/${id}${versionId ? `?version=${versionId}` : ''}`);
    setM(r); setVer(r.selected); setRules(r.selected.rules); setFilters(r.selected.filters);
  };
  useEffect(() => {
    load().catch((e) => setErrors(errs(e)));
    api<any[]>('/api/messaging/tables').then(setTables).catch(() => setTables([]));
    api('/api/messaging/catalogue').then(setCatalogue).catch(() => setCatalogue(null));
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const draft = ver?.status === 'Draft' && canEdit;
  const src = m?.mapping.sourceFormat, tgt = m?.mapping.targetFormat;
  const edit = () => ({ startFrom: ver.startFrom, template: ver.template, sampleInput: ver.sampleInput, changeNote: ver.changeNote, rules, filters });

  // Live preview: re-run shortly after any change.
  useEffect(() => {
    if (!ver) return;
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => {
      api('/api/messaging/preview', { body: { source: src, target: tgt, version: edit() } }).then(setPreview).catch(() => undefined);
    }, 400);
    return () => window.clearTimeout(timer.current);
  }, [ver, rules, filters]); // eslint-disable-line react-hooks/exhaustive-deps

  const act = async (fn: () => Promise<any>, ok: string, reloadVersion?: string) => {
    setErrors(null); setMsg(null);
    try { const r = await fn(); setMsg(ok); await load(reloadVersion ?? r?.id ?? ver.id); } catch (x) { setErrors(errs(x)); }
  };
  const save = () => act(() => api(`/api/messaging/versions/${ver.id}/draft`, { body: edit() }), 'Draft saved.');
  const setRule = (i: number, v: Partial<Rule>) => setRules(rules.map((r, j) => (j === i ? { ...r, ...v } : r)));
  const move = (i: number, d: number) => { const r = [...rules]; const [x] = r.splice(i, 1); r.splice(i + d, 0, x); setRules(r); };
  const pick = (path: string) => {
    if (!draft) return;
    if (!focus) { setRules([...rules, { target: '', source: path, function: 'Copy', argument: null, translationTableId: null, conditionSource: null, conditionOperator: null, conditionValue: null }]); return; }
    const r = rules[focus.row];
    if (focus.field === 'source') setRule(focus.row, { source: r.function === 'Join' && r.source ? `${r.source} + ${path}` : path });
    else if (focus.field === 'cond') setRule(focus.row, { conditionSource: path, conditionOperator: r.conditionOperator ?? 'Equals' });
  };
  const loadDicom = async (f?: File) => { if (!f) return; try { setVer({ ...ver, sampleInput: await dicomFileToJson(f) }); } catch (x) { setErrors(errs(x)); } };
  const targetList = tgt === 'Hl7v2' ? 'hl7-fields' : tgt === 'Dicom' ? 'dicom-attrs' : undefined;
  const isAuthor = ver && (ver.editedBy === me?.userName || ver.submittedBy === me?.userName);

  if (!m || !ver) return <div className="stack-lg"><MsgNav /><Errors list={errors} />{!errors && <p>Loading…</p>}</div>;
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>{m.mapping.name}</h1><p className="muted">{FORMAT_LABEL[src]} → {FORMAT_LABEL[tgt]}. Drag a field from the input on the left onto a rule, or click a field to add a rule.</p></div>
      <MsgNav />
      <section className="card stack">
        <div className="row-between">
          <div className="row">
            <label>Version<select value={ver.id} onChange={(e) => load(e.target.value)}>{m.versions.map((v: any) => <option key={v.id} value={v.id}>v{v.number} – {v.status === 'AwaitingApproval' ? 'waiting for approval' : v.status.toLowerCase()}</option>)}</select></label>
            <span className={`badge ${ver.status === 'Live' ? 'ok' : ver.status === 'AwaitingApproval' ? 'warn' : ''}`}>{ver.status === 'AwaitingApproval' ? 'Waiting for approval' : ver.status}</span>
            <span className="small muted">Edited by {ver.editedBy} {nzDateTime(ver.editedAt)}{ver.approvedBy ? ` · approved by ${ver.approvedBy} ${nzDateTime(ver.approvedAt)}` : ''}</span>
          </div>
          {canEdit && <div className="row">
            {ver.status === 'Draft' && <><button className="btn btn-primary" onClick={save}>Save draft</button>
              <button className="btn btn-light" onClick={() => act(async () => { await api(`/api/messaging/versions/${ver.id}/draft`, { body: edit() }); return api(`/api/messaging/versions/${ver.id}/submit`, { body: {} }); }, 'Sent for approval. A second integration administrator approves it.')}>Send for approval</button></>}
            {ver.status === 'AwaitingApproval' && <><button className="btn btn-primary" disabled={isAuthor} title={isAuthor ? 'A second person must approve' : ''} onClick={() => act(() => api(`/api/messaging/versions/${ver.id}/approve`, { body: {} }), 'Approved – this version is now live.')}>Approve and go live</button>
              <button className="btn btn-light" onClick={() => act(() => api(`/api/messaging/versions/${ver.id}/reject`, { body: { note: reason } }), 'Returned to draft.')}>Return to draft</button></>}
            {(ver.status === 'Live' || ver.status === 'Retired') && !m.versions.some((v: any) => v.status === 'Draft' || v.status === 'AwaitingApproval') &&
              <button className="btn btn-light" onClick={() => act(() => api(`/api/messaging/mappings/${id}/new-draft`, { body: { fromVersionId: ver.id } }), 'New draft created.')}>Make a new draft from v{ver.number}</button>}
            {ver.status === 'Retired' && <button className="btn btn-warn" disabled={reason.trim().length < 5} onClick={() => act(() => api(`/api/messaging/versions/${ver.id}/rollback`, { body: { reason } }), `v${ver.number} is live again.`)}>Put v{ver.number} back live</button>}
            {(ver.status === 'Retired' || ver.status === 'AwaitingApproval') && <input aria-label="Reason or note" value={reason} onChange={(e) => setReason(e.target.value)} placeholder={ver.status === 'Retired' ? 'Reason for rolling back' : 'Note (optional)'} />}
          </div>}
        </div>
        {draft && <label>What changed in this version<input value={ver.changeNote ?? ''} onChange={(e) => setVer({ ...ver, changeNote: e.target.value })} /></label>}
        <Errors list={errors} />
        {msg && <p className="ok-text">{msg}</p>}
      </section>

      <div className="mapper">
        <section className="card stack mapper-col">
          <h2>Input sample</h2>
          <p className="hint">Synthetic test data only.</p>
          {draft && <div className="row">
            <button className="btn btn-light btn-small" onClick={async () => setVer({ ...ver, sampleInput: (await api(`/api/messaging/samples/${src}`)).sample })}>Load example</button>
            {src === 'Dicom' && <label className="btn btn-light btn-small file-btn">Load a DICOM file<input type="file" className="sr-only" onChange={(e) => { loadDicom(e.target.files?.[0]); e.target.value = ''; }} /></label>}</div>}
          <textarea rows={6} className="mono" value={ver.sampleInput ?? ''} onChange={(e) => setVer({ ...ver, sampleInput: e.target.value })} readOnly={!draft} aria-label="Sample input" />
          {preview?.sourceProblems?.length > 0 && <p className="warn-text small">{preview.sourceProblems.join(' ')}</p>}
          <input aria-label="Find a field" placeholder="Find a field…" value={filter} onChange={(e) => setFilter(e.target.value)} />
          <div className="tree-scroll"><TreeView nodes={(preview?.sourceTree ?? []) as DocNode[]} onPick={pick} filter={filter} /></div>
        </section>

        <section className="card stack mapper-col wide">
          <h2>Rules</h2>
          <div className="stack-sm"><strong>Only map messages where…</strong> <span className="hint">(leave empty to map every message)</span>
            {filters.map((f, i) => (
              <div className="cond-row" key={i}>
                <DropInput label="Field" value={f.source} onChange={(v) => setFilters(filters.map((x, j) => j === i ? { ...x, source: v } : x))} placeholder={PATH_HINT[src]} />
                <select aria-label="Comparison" value={f.operator} onChange={(e) => setFilters(filters.map((x, j) => j === i ? { ...x, operator: e.target.value } : x))} disabled={!draft}>{OPERATORS.map((o) => <option key={o} value={o}>{OP_LABEL[o]}</option>)}</select>
                {!['IsEmpty', 'NotEmpty'].includes(f.operator) && <input aria-label="Value" value={f.value ?? ''} onChange={(e) => setFilters(filters.map((x, j) => j === i ? { ...x, value: e.target.value } : x))} readOnly={!draft} />}
                {draft && <button className="btn btn-link" onClick={() => setFilters(filters.filter((_, j) => j !== i))}>✕</button>}
              </div>))}
            {draft && <div><button className="btn btn-light btn-small" onClick={() => setFilters([...filters, { source: '', operator: 'Equals', value: '' }])}>+ Filter</button></div>}
          </div>
          {src !== tgt ? <label>Output starts from this template ({FORMAT_LABEL[tgt]})<textarea rows={3} className="mono" value={ver.template ?? ''} onChange={(e) => setVer({ ...ver, template: e.target.value })} readOnly={!draft} /></label>
            : <label>Output starts as<select value={ver.startFrom} onChange={(e) => setVer({ ...ver, startFrom: e.target.value })} disabled={!draft}><option value="CopySource">a copy of the input (change only what the rules say)</option><option value="Template">the template below</option></select>
              {ver.startFrom === 'Template' && <textarea rows={3} className="mono" value={ver.template ?? ''} onChange={(e) => setVer({ ...ver, template: e.target.value })} readOnly={!draft} />}</label>}
          <table className="results compact rules"><thead><tr><th>Set this output field</th><th>From input</th><th>How</th><th>Only when (optional)</th><th /></tr></thead>
            <tbody>{rules.map((r, i) => (
              <tr key={i} className={focus?.row === i ? 'focus' : ''}>
                <td><DropInput label="Target field" value={r.target} onChange={(v) => setRule(i, { target: v })} placeholder={PATH_HINT[tgt]} list={targetList} /></td>
                <td onFocus={() => setFocus({ row: i, field: 'source' })}>{['Constant', 'Clear'].includes(r.function) ? <span className="muted small">—</span>
                  : <DropInput label="Source field" value={r.source ?? ''} onChange={(v) => setRule(i, { source: v })} placeholder={PATH_HINT[src]} append={r.function === 'Join'} />}</td>
                <td><select aria-label="How" value={r.function} onChange={(e) => setRule(i, { function: e.target.value })} disabled={!draft}>{Object.entries(FUNCTIONS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select>
                  {ARG_HINT[r.function] && <input aria-label="Setting" value={r.argument ?? ''} onChange={(e) => setRule(i, { argument: e.target.value })} placeholder={ARG_HINT[r.function]} readOnly={!draft} />}
                  {r.function === 'Translate' && <select aria-label="Translation table" value={r.translationTableId ?? ''} onChange={(e) => setRule(i, { translationTableId: e.target.value || null })} disabled={!draft}><option value="">Choose table…</option>{tables.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>}</td>
                <td onFocus={() => setFocus({ row: i, field: 'cond' })}>
                  <DropInput label="Condition field" value={r.conditionSource ?? ''} onChange={(v) => setRule(i, { conditionSource: v || null, conditionOperator: v ? r.conditionOperator ?? 'Equals' : null })} placeholder="field" />
                  {r.conditionSource && <><select aria-label="Condition" value={r.conditionOperator ?? 'Equals'} onChange={(e) => setRule(i, { conditionOperator: e.target.value })} disabled={!draft}>{OPERATORS.map((o) => <option key={o} value={o}>{OP_LABEL[o]}</option>)}</select>
                    {!['IsEmpty', 'NotEmpty'].includes(r.conditionOperator ?? '') && <input aria-label="Condition value" value={r.conditionValue ?? ''} onChange={(e) => setRule(i, { conditionValue: e.target.value })} readOnly={!draft} />}</>}</td>
                <td className="actions">{draft && <>{i > 0 && <button className="btn btn-link" onClick={() => move(i, -1)} aria-label="Move up">↑</button>}{i < rules.length - 1 && <button className="btn btn-link" onClick={() => move(i, 1)} aria-label="Move down">↓</button>}
                  <button className="btn btn-link" onClick={() => setRules(rules.filter((_, j) => j !== i))} aria-label="Remove rule">✕</button></>}</td>
              </tr>))}</tbody></table>
          {draft && <div><button className="btn btn-light btn-small" onClick={() => setRules([...rules, { target: '', source: '', function: 'Copy', argument: null, translationTableId: null, conditionSource: null, conditionOperator: null, conditionValue: null }])}>+ Rule</button></div>}
          {catalogue && <><datalist id="hl7-fields">{catalogue.hl7.flatMap((s: any) => s.fields).map((f: any) => <option key={f.path} value={f.path}>{f.name}</option>)}</datalist>
            <datalist id="dicom-attrs">{catalogue.dicom.map((d: any) => <option key={d.path} value={d.path}>{d.tag} {d.name}</option>)}</datalist></>}
        </section>

        <section className="card stack mapper-col">
          <div className="row-between"><h2>Output preview</h2><div className="tabs compact-tabs"><button className={outView === 'tree' ? 'on' : ''} onClick={() => setOutView('tree')}>Readable</button><button className={outView === 'raw' ? 'on' : ''} onClick={() => setOutView('raw')}>Raw</button></div></div>
          {!preview ? <p className="muted">Working…</p> : <>
            {preview.filteredOut && <p className="warn-text">This sample would be filtered out: {preview.steps.join(' ')}</p>}
            <Errors list={preview.errors} />
            {preview.warnings.length > 0 && <ul className="warn-text small">{preview.warnings.map((w: string, i: number) => <li key={i}>{w}</li>)}</ul>}
            {preview.output && (outView === 'tree' ? <div className="tree-scroll"><TreeView nodes={preview.outputTree} draggable={false} /></div> : <pre className="mono-box raw">{preview.output.replaceAll('\r', '\n')}</pre>)}
            {preview.errors.length === 0 && !preview.filteredOut && <p className="ok-text small">Maps cleanly ({preview.steps.length} rule(s)).</p>}
          </>}
          <p className="hint">Used in flows: {m.mapping.usedInFlows}. <Link to="/messaging/flows">Open flows</Link> to test the whole path.</p>
        </section>
      </div>
    </div>
  );
}
