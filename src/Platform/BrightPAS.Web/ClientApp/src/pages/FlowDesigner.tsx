import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import { Condition, ConditionsEditor, Errors, errs, FORMAT_LABEL, MsgNav } from '../components/MsgShared';
import { dicomFileToJson } from './Messaging';

type Kind = 'Input' | 'Filter' | 'Decision' | 'Mapper' | 'Output';
interface FNode { key: string; kind: Kind; label: string; x: number; y: number; interfaceId: string | null; mappingId: string | null; match: 'All' | 'Any'; conditions: Condition[] }
interface FEdge { from: string; to: string; branch: 'Next' | 'Yes' | 'No' }

const W = 176, H = 66;
const KIND_HELP: Record<Kind, string> = {
  Input: 'Where the message arrives (inbound interface).', Filter: 'Stops the message unless the conditions match.', Decision: 'Sends the message down the Yes or No path.',
  Mapper: 'Changes the message with a mapping (fields, codes, format).', Output: 'Where the message is sent (outbound interface).',
};
const newKey = () => Math.random().toString(36).slice(2, 10);

export function MessagingFlows() {
  const { can } = useSession();
  const [list, setList] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[] | null>(null);
  useEffect(() => { api<any[]>('/api/messaging/flows').then(setList).catch((e) => setErrors(errs(e))); }, []);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Message flows</h1><p className="muted">Draw how messages travel: input → filters and decisions → mappers → outputs. Test with a sample before switching a flow on.</p></div>
      <MsgNav />
      <Errors list={errors} />
      {can(Perm.Integration) && <div><Link className="btn btn-primary" to="/messaging/flows/new">New flow</Link></div>}
      {list && (list.length === 0 ? <p className="muted">No flows yet.</p> :
        <table className="results"><thead><tr><th>Flow</th><th>From</th><th>To</th><th>Status</th><th>Changed</th></tr></thead>
          <tbody>{list.map((f) => <tr key={f.id}><td><Link to={`/messaging/flows/${f.id}`}><strong>{f.name}</strong></Link><div className="small muted">{f.description}</div></td>
            <td>{f.input ?? '—'}</td><td className="small">{f.outputs.join(', ') || '—'}</td><td>{f.isActive ? <span className="badge ok">On</span> : <span className="badge off">Off</span>}</td>
            <td className="small">{nzDateTime(f.updatedAt)} {f.updatedBy}</td></tr>)}</tbody></table>)}
    </div>
  );
}

export function FlowEditor() {
  const { id } = useParams();
  const nav = useNavigate();
  const { can } = useSession();
  const canEdit = can(Perm.Integration);
  const svgRef = useRef<SVGSVGElement>(null);
  const [meta, setMeta] = useState({ id: null as string | null, name: '', description: '', isActive: false });
  const [nodes, setNodes] = useState<FNode[]>([]);
  const [edges, setEdges] = useState<FEdge[]>([]);
  const [sel, setSel] = useState<{ node?: string; edge?: number } | null>(null);
  const [drag, setDrag] = useState<{ key: string; dx: number; dy: number } | null>(null);
  const [link, setLink] = useState<{ from: string; branch: FEdge['branch']; x: number; y: number } | null>(null);
  const [ifaces, setIfaces] = useState<any[]>([]);
  const [maps, setMaps] = useState<any[]>([]);
  const [errors, setErrors] = useState<string[] | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [sample, setSample] = useState('');
  const [useDrafts, setUseDrafts] = useState(true);
  const [test, setTest] = useState<any>(null);
  const [showOut, setShowOut] = useState<string | null>(null);

  useEffect(() => {
    api<any[]>('/api/messaging/interfaces').then(setIfaces).catch(() => setIfaces([]));
    api<any[]>('/api/messaging/mappings').then(setMaps).catch(() => setMaps([]));
    if (id && id !== 'new') api(`/api/messaging/flows/${id}`).then((f) => {
      setMeta({ id: f.id, name: f.name, description: f.description ?? '', isActive: f.isActive });
      setNodes(f.nodes.map((n: any) => ({ ...n, conditions: n.conditions ?? [] })));
      setEdges(f.edges);
    }).catch((e) => setErrors(errs(e)));
    else setNodes([{ key: newKey(), kind: 'Input', label: 'Input', x: 40, y: 160, interfaceId: null, mappingId: null, match: 'All', conditions: [] }]);
  }, [id]);

  const byKey = useMemo(() => Object.fromEntries(nodes.map((n) => [n.key, n])), [nodes]);
  const iface = (i: string | null) => ifaces.find((x) => x.id === i);
  const mapping = (m: string | null) => maps.find((x) => x.id === m);
  const inputFormat = iface(nodes.find((n) => n.kind === 'Input')?.interfaceId ?? null)?.format ?? 'Hl7v2';
  const outcome = (key: string) => test?.steps?.filter((s: any) => s.nodeKey === key).map((s: any) => s.outcome).join(', ');
  const selected = sel?.node ? byKey[sel.node] : null;

  const pt = (e: React.PointerEvent) => {
    const svg = svgRef.current!;
    const p = svg.createSVGPoint(); p.x = e.clientX; p.y = e.clientY;
    const r = p.matrixTransform(svg.getScreenCTM()!.inverse());
    return { x: r.x, y: r.y };
  };
  const update = (key: string, v: Partial<FNode>) => setNodes((ns) => ns.map((n) => (n.key === key ? { ...n, ...v } : n)));
  const add = (kind: Kind) => {
    const n: FNode = { key: newKey(), kind, label: kind === 'Decision' ? 'Decision' : kind, x: 60 + (nodes.length % 5) * 200, y: 40 + Math.floor(nodes.length / 5) * 110 + (nodes.length % 2) * 40, interfaceId: null, mappingId: null, match: 'All', conditions: kind === 'Filter' || kind === 'Decision' ? [{ source: '', operator: 'Equals', value: '' }] : [] };
    setNodes([...nodes, n]); setSel({ node: n.key });
  };
  const remove = () => {
    if (sel?.node) { const k = sel.node; setNodes(nodes.filter((n) => n.key !== k)); setEdges(edges.filter((e) => e.from !== k && e.to !== k)); }
    if (sel?.edge !== undefined) setEdges(edges.filter((_, i) => i !== sel.edge));
    setSel(null);
  };
  const onMove = (e: React.PointerEvent) => {
    const p = pt(e);
    if (drag) update(drag.key, { x: Math.max(0, Math.round((p.x - drag.dx) / 10) * 10), y: Math.max(0, Math.round((p.y - drag.dy) / 10) * 10) });
    if (link) setLink({ ...link, x: p.x, y: p.y });
  };
  const onUp = (e: React.PointerEvent) => {
    if (link) {
      const p = pt(e);
      const target = nodes.find((n) => p.x >= n.x && p.x <= n.x + W && p.y >= n.y && p.y <= n.y + H && n.key !== link.from && n.kind !== 'Input');
      if (target && !edges.some((x) => x.from === link.from && x.to === target.key && x.branch === link.branch)) setEdges([...edges, { from: link.from, to: target.key, branch: link.branch }]);
    }
    setDrag(null); setLink(null);
  };
  const outPort = (n: FNode, b: FEdge['branch']) => ({ x: n.x + W, y: n.y + (b === 'Yes' ? H * 0.3 : b === 'No' ? H * 0.72 : H / 2) });
  const curve = (a: { x: number; y: number }, b: { x: number; y: number }) => `M ${a.x} ${a.y} C ${a.x + 60} ${a.y}, ${b.x - 60} ${b.y}, ${b.x} ${b.y}`;

  const body = () => ({ id: meta.id, name: meta.name, description: meta.description, isActive: meta.isActive, nodes, edges });
  const save = async () => {
    setErrors(null); setMsg(null);
    try {
      const r = await api('/api/messaging/flows', { body: body() });
      setMsg(r.warnings?.length ? `Saved (switched off). Before it can be switched on: ${r.warnings.join(' ')}` : 'Saved.');
      if (!meta.id) { setMeta({ ...meta, id: r.id }); nav(`/messaging/flows/${r.id}`, { replace: true }); }
    } catch (x) { setErrors(errs(x)); }
  };
  const check = async () => { setErrors(null); setMsg(null); const r = await api('/api/messaging/flows/check', { body: body() }); if (r.errors.length) setErrors(r.errors); else setMsg('The flow is complete – no problems found.'); };
  const runTest = async () => {
    setErrors(null); setTest(null);
    try { const r = await api('/api/messaging/flows/test', { body: { flow: body(), sample, useDrafts } }); if (r.errors?.length) setErrors(r.errors); setTest(r); } catch (x) { setErrors(errs(x)); }
  };
  const loadSample = async () => setSample((await api(`/api/messaging/samples/${inputFormat}`)).sample);
  const loadDicom = async (f?: File) => { if (!f) return; try { setSample(await dicomFileToJson(f)); } catch (x) { setErrors(errs(x)); } };

  const maxX = Math.max(1000, ...nodes.map((n) => n.x + W + 60));
  const maxY = Math.max(420, ...nodes.map((n) => n.y + H + 60));

  return (
    <div className="stack-lg">
      <div className="page-head"><h1>{meta.name || 'New flow'}</h1><p className="muted">Drag steps to arrange them. Drag from a step's right-hand dot to the next step to connect them. Decisions have a Yes (top) and No (bottom) dot.</p></div>
      <MsgNav />
      <section className="card stack">
        <div className="form-grid">
          <label>Flow name<input value={meta.name} onChange={(e) => setMeta({ ...meta, name: e.target.value })} disabled={!canEdit} placeholder="e.g. Endoscopy images → lab system" /></label>
          <label>Description<input value={meta.description} onChange={(e) => setMeta({ ...meta, description: e.target.value })} disabled={!canEdit} /></label>
        </div>
        <div className="row">
          {canEdit && (['Input', 'Filter', 'Decision', 'Mapper', 'Output'] as Kind[]).map((k) => <button key={k} className={`btn btn-light btn-small palette-${k}`} onClick={() => add(k)} title={KIND_HELP[k]}>+ {k}</button>)}
          {canEdit && sel && <button className="btn btn-link danger" onClick={remove}>Delete selected</button>}
        </div>
        <div className="canvas-wrap">
          <svg ref={svgRef} className="flow-canvas" viewBox={`0 0 ${maxX} ${maxY}`} width={maxX} height={maxY} onPointerMove={onMove} onPointerUp={onUp} onPointerLeave={onUp}
            onPointerDown={(e) => { if (e.target === svgRef.current) setSel(null); }} role="application" aria-label="Flow canvas">
            <defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z" className="arrow-head" /></marker></defs>
            {edges.map((e, i) => {
              const a = byKey[e.from], b = byKey[e.to];
              if (!a || !b) return null;
              const p1 = outPort(a, e.branch), p2 = { x: b.x, y: b.y + H / 2 };
              return (
                <g key={i} className={`edge ${sel?.edge === i ? 'sel' : ''} branch-${e.branch}`} onPointerDown={(ev) => { ev.stopPropagation(); setSel({ edge: i }); }}>
                  <path d={curve(p1, p2)} className="edge-hit" />
                  <path d={curve(p1, p2)} className="edge-line" markerEnd="url(#arrow)" />
                  {e.branch !== 'Next' && <text x={(p1.x + p2.x) / 2} y={(p1.y + p2.y) / 2 - 6} className="edge-label">{e.branch}</text>}
                </g>);
            })}
            {link && byKey[link.from] && <path d={curve(outPort(byKey[link.from], link.branch), link)} className="edge-line temp" />}
            {nodes.map((n) => {
              const sub = n.kind === 'Input' || n.kind === 'Output' ? iface(n.interfaceId)?.name ?? 'choose interface'
                : n.kind === 'Mapper' ? mapping(n.mappingId)?.name ?? 'choose mapping'
                : n.conditions.filter((c) => c.source).map((c) => `${c.source} ${c.operator === 'Equals' ? '=' : c.operator} ${c.value ?? ''}`).join(n.match === 'All' ? ' and ' : ' or ') || 'add conditions';
              const o = outcome(n.key);
              return (
                <g key={n.key} transform={`translate(${n.x},${n.y})`} className={`fnode kind-${n.kind} ${sel?.node === n.key ? 'sel' : ''} ${o ? `run-${o.split(',')[0].replace(' ', '')}` : ''}`}
                  onPointerDown={(e) => { e.stopPropagation(); setSel({ node: n.key }); if (canEdit) { const p = pt(e); setDrag({ key: n.key, dx: p.x - n.x, dy: p.y - n.y }); } }}>
                  <rect width={W} height={H} rx={n.kind === 'Decision' ? 18 : 8} className="fnode-box" />
                  <text x={10} y={20} className="fnode-kind">{n.kind.toUpperCase()}</text>
                  <text x={10} y={38} className="fnode-label">{n.label.slice(0, 22)}</text>
                  <text x={10} y={55} className="fnode-sub">{sub.slice(0, 28)}</text>
                  {o && <text x={W - 8} y={20} textAnchor="end" className="fnode-run">{o}</text>}
                  {n.kind !== 'Input' && <circle cx={0} cy={H / 2} r={6} className="port in" />}
                  {canEdit && n.kind !== 'Output' && (n.kind === 'Decision'
                    ? (['Yes', 'No'] as const).map((b) => <circle key={b} cx={W} cy={b === 'Yes' ? H * 0.3 : H * 0.72} r={8} className={`port out ${b}`} onPointerDown={(e) => { e.stopPropagation(); const p = pt(e); setLink({ from: n.key, branch: b, x: p.x, y: p.y }); }}><title>{b}</title></circle>)
                    : <circle cx={W} cy={H / 2} r={8} className="port out" onPointerDown={(e) => { e.stopPropagation(); const p = pt(e); setLink({ from: n.key, branch: 'Next', x: p.x, y: p.y }); }}><title>Drag to the next step</title></circle>)}
                </g>);
            })}
          </svg>
        </div>
        {selected && (
          <div className="card subtle stack">
            <strong>{selected.kind}</strong><span className="hint">{KIND_HELP[selected.kind]}</span>
            <div className="form-grid">
              <label>Label<input value={selected.label} onChange={(e) => update(selected.key, { label: e.target.value })} disabled={!canEdit} maxLength={60} /></label>
              {(selected.kind === 'Input' || selected.kind === 'Output') && <label>Interface<select value={selected.interfaceId ?? ''} onChange={(e) => update(selected.key, { interfaceId: e.target.value || null })} disabled={!canEdit}>
                <option value="">Choose…</option>{ifaces.filter((i) => i.transport !== 'Monitored' && i.direction === (selected.kind === 'Input' ? 'Inbound' : 'Outbound')).map((i) => <option key={i.id} value={i.id}>{i.name} ({FORMAT_LABEL[i.format]})</option>)}</select></label>}
              {selected.kind === 'Mapper' && <label>Mapping<select value={selected.mappingId ?? ''} onChange={(e) => update(selected.key, { mappingId: e.target.value || null })} disabled={!canEdit}>
                <option value="">Choose…</option>{maps.map((m) => <option key={m.id} value={m.id}>{m.name} ({FORMAT_LABEL[m.sourceFormat]} → {FORMAT_LABEL[m.targetFormat]}){m.live ? '' : ' – not live yet'}</option>)}</select>
                {selected.mappingId && <Link to={`/messaging/mappings/${selected.mappingId}`} className="small">Open mapping</Link>}</label>}
              {(selected.kind === 'Filter' || selected.kind === 'Decision') && <label>Match<select value={selected.match} onChange={(e) => update(selected.key, { match: e.target.value as any })} disabled={!canEdit}><option value="All">all conditions</option><option value="Any">any condition</option></select></label>}
            </div>
            {(selected.kind === 'Filter' || selected.kind === 'Decision') && <ConditionsEditor items={selected.conditions} onChange={(c) => update(selected.key, { conditions: c })} format={inputFormat} />}
          </div>)}
        <Errors list={errors} />
        {msg && <p className="ok-text">{msg}</p>}
        {canEdit && <div className="row">
          <label className="check"><input type="checkbox" checked={meta.isActive} onChange={(e) => setMeta({ ...meta, isActive: e.target.checked })} /> Switched on (live messages run through this flow)</label>
          <button className="btn btn-primary" onClick={save}>Save flow</button><button className="btn btn-light" onClick={check}>Check</button>
          {meta.id && !meta.isActive && <button className="btn btn-link danger" onClick={async () => { try { await api(`/api/messaging/flows/${meta.id}/delete`, { body: {} }); nav('/messaging/flows'); } catch (x) { setErrors(errs(x)); } }}>Delete flow</button>}
        </div>}
      </section>

      <section className="card stack">
        <h2>Test harness</h2>
        <p className="hint">Runs the flow as drawn (even unsaved) against a sample. Nothing is stored or sent. Use synthetic test patients only.</p>
        <div className="row">
          <button className="btn btn-light btn-small" onClick={loadSample}>Load a {FORMAT_LABEL[inputFormat]} sample</button>
          {inputFormat === 'Dicom' && <label className="btn btn-light btn-small file-btn">Load a DICOM file (.dcm)<input type="file" className="sr-only" onChange={(e) => { loadDicom(e.target.files?.[0]); e.target.value = ''; }} /></label>}
          <label className="check"><input type="checkbox" checked={useDrafts} onChange={(e) => setUseDrafts(e.target.checked)} /> Use draft mappings (test changes before approval)</label>
        </div>
        <textarea rows={8} className="mono" value={sample} onChange={(e) => setSample(e.target.value)} placeholder="Paste a message" aria-label="Sample message" />
        <div><button className="btn btn-primary" onClick={runTest} disabled={!sample.trim()}>Run test</button></div>
        {test && <div className="stack">
          <ol className="timeline">{test.steps.map((s: any, i: number) => <li key={i} className={`run-${s.outcome.replace(' ', '')}`}><strong>{s.label}</strong> <span className="badge">{s.outcome}</span>{s.detail && <div className="small">{s.detail}</div>}
            {s.content && <button className="btn btn-link btn-small" onClick={() => setShowOut(showOut === `s${i}` ? null : `s${i}`)}>{showOut === `s${i}` ? 'Hide' : 'Show'} message here</button>}
            {showOut === `s${i}` && <pre className="mono-box raw">{s.content.replaceAll('\r', '\n')}</pre>}</li>)}</ol>
          <h3>Would send</h3>
          {test.outputs.length === 0 ? <p className="muted">Nothing – the message stopped before reaching an output.</p> : test.outputs.map((o: any, i: number) => (
            <div key={i} className="card subtle stack-sm"><strong>{o.interface}</strong> <span className="muted small">{FORMAT_LABEL[o.format]}</span>{o.problems.length > 0 && <p className="error">{o.problems.join(' ')}</p>}<pre className="mono-box raw">{o.content.replaceAll('\r', '\n')}</pre></div>))}
        </div>}
      </section>
    </div>
  );
}
