import { useState } from 'react';
import { NavLink } from 'react-router';

export const FORMATS = ['Hl7v2', 'Fhir', 'Json', 'Dicom', 'Xml', 'X12', 'Text'] as const;
export type Format = typeof FORMATS[number];
export const FORMAT_LABEL: Record<string, string> = { Hl7v2: 'HL7 v2', Fhir: 'FHIR (JSON)', Json: 'JSON / REST', Dicom: 'DICOM', Xml: 'CDA / XML', X12: 'X12 EDI', Text: 'Plain text' };
export const TRANSPORT_LABEL: Record<string, string> = {
  SqlQuery: 'Receive – read another database (SQL SELECT)', SqlWrite: 'Send – write to another database (SQL)',
  MllpListen: 'Receive – MLLP (HL7 listener)', DicomListen: 'Receive – DICOM node (devices)', HttpReceive: 'Receive – REST API (others post to BrightPAS)', FolderWatch: 'Receive – watch a folder',
  MllpSend: 'Send – MLLP (HL7)', HttpPost: 'Send – REST / FHIR (post to a web address)', FolderWrite: 'Send – write files to a folder', Monitored: 'Recorded from another BrightPAS module',
};
export const OPERATORS = ['Equals', 'NotEquals', 'Contains', 'StartsWith', 'In', 'IsEmpty', 'NotEmpty'] as const;
export const OP_LABEL: Record<string, string> = { Equals: 'equals', NotEquals: 'is not', Contains: 'contains', StartsWith: 'starts with', In: 'is one of (a,b,c)', IsEmpty: 'is empty', NotEmpty: 'has a value' };
export const PATH_HINT: Record<string, string> = {
  Hl7v2: 'e.g. PID-5.1, MSH-9.1, OBX[*]-5', Fhir: 'e.g. name[0].family, identifier[0].value', Json: 'e.g. patient.nhi, results[*].value',
  Dicom: 'e.g. PatientName, (0008,0050), RequestAttributesSequence[0].RequestedProcedureID', Xml: 'e.g. ClinicalDocument/recordTarget/patientRole/id/@extension',
  X12: 'e.g. NM1[2]-3, CLM-1', Text: 'text',
};

export interface DocNode { path: string; label: string; value: string | null; children: DocNode[] }
export interface Condition { source: string; operator: string; value: string | null }

export function MsgNav() {
  return (
    <nav className="tabs msg-tabs" aria-label="Messaging">
      <NavLink to="/messaging" end>Dashboard</NavLink>
      <NavLink to="/messaging/flows">Flows</NavLink>
      <NavLink to="/messaging/interfaces">Interfaces</NavLink>
      <NavLink to="/messaging/mappings">Mappings</NavLink>
      <NavLink to="/messaging/tables">Translation tables</NavLink>
      <NavLink to="/messaging/messages">Messages</NavLink>
      <NavLink to="/messaging/settings">Settings</NavLink>
    </nav>
  );
}

const statusClass: Record<string, string> = {
  Processed: 'ok', Sent: 'ok', Recorded: 'ok', Received: '', Queued: '', Sending: '', Filtered: 'off', Failed: 'warn', DeadLetter: 'warn', Rejected: 'warn',
};
export function StatusBadge({ status }: { status: string }) {
  return <span className={`badge ${statusClass[status] ?? ''}`}>{status === 'DeadLetter' ? 'Gave up' : status}</span>;
}

/** Readable message tree. Nodes can be dragged onto mapping fields, or clicked to pick them. */
export function TreeView({ nodes, onPick, draggable = true, filter }: { nodes: DocNode[]; onPick?: (path: string) => void; draggable?: boolean; filter?: string }) {
  const f = (filter ?? '').toLowerCase();
  const match = (n: DocNode): boolean => !f || n.label.toLowerCase().includes(f) || (n.value ?? '').toLowerCase().includes(f) || n.children.some(match);
  return <ul className="tree">{nodes.filter(match).map((n) => <TreeNode key={n.path} n={n} onPick={onPick} draggable={draggable} match={match} />)}</ul>;
}

function TreeNode({ n, onPick, draggable, match }: { n: DocNode; onPick?: (p: string) => void; draggable: boolean; match: (n: DocNode) => boolean }) {
  const [open, setOpen] = useState(true);
  const has = n.children.length > 0;
  return (
    <li>
      <div className="tree-row">
        {has ? <button type="button" className="tree-toggle" aria-label={open ? 'Collapse' : 'Expand'} onClick={() => setOpen(!open)}>{open ? '▾' : '▸'}</button> : <span className="tree-toggle" />}
        <span className={`tree-label ${onPick ? 'pickable' : ''}`} draggable={draggable} title={`${n.path} – drag onto a mapping field, or click to use it`}
          onDragStart={(e) => { e.dataTransfer.setData('text/plain', n.path); e.dataTransfer.effectAllowed = 'copy'; }}
          onClick={() => onPick?.(n.path)} onKeyDown={(e) => { if (e.key === 'Enter') onPick?.(n.path); }} role={onPick ? 'button' : undefined} tabIndex={onPick ? 0 : undefined}>
          {n.label}
        </span>
        {n.value !== null && n.value !== '' && <span className="tree-value">{n.value}</span>}
      </div>
      {has && open && <ul>{n.children.filter(match).map((c) => <TreeNode key={c.path} n={c} onPick={onPick} draggable={draggable} match={match} />)}</ul>}
    </li>
  );
}

/** Text input that accepts a field dragged from a message tree. */
export function DropInput({ value, onChange, placeholder, label, list, append }: { value: string; onChange: (v: string) => void; placeholder?: string; label: string; list?: string; append?: boolean }) {
  const [over, setOver] = useState(false);
  return (
    <input aria-label={label} className={`drop ${over ? 'over' : ''}`} value={value} placeholder={placeholder} list={list}
      onChange={(e) => onChange(e.target.value)}
      onDragOver={(e) => { e.preventDefault(); setOver(true); }} onDragLeave={() => setOver(false)}
      onDrop={(e) => { e.preventDefault(); setOver(false); const p = e.dataTransfer.getData('text/plain'); if (p) onChange(append && value ? `${value} + ${p}` : p); }} />
  );
}

export function ConditionsEditor({ items, onChange, format }: { items: Condition[]; onChange: (c: Condition[]) => void; format: string }) {
  const set = (i: number, c: Partial<Condition>) => onChange(items.map((x, j) => (j === i ? { ...x, ...c } : x)));
  return (
    <div className="stack-sm">
      {items.map((c, i) => (
        <div className="cond-row" key={i}>
          <DropInput label="Field" value={c.source} onChange={(v) => set(i, { source: v })} placeholder={PATH_HINT[format] ?? 'field'} />
          <select aria-label="Comparison" value={c.operator} onChange={(e) => set(i, { operator: e.target.value })}>{OPERATORS.map((o) => <option key={o} value={o}>{OP_LABEL[o]}</option>)}</select>
          {!['IsEmpty', 'NotEmpty'].includes(c.operator) && <input aria-label="Value" value={c.value ?? ''} onChange={(e) => set(i, { value: e.target.value })} />}
          <button type="button" className="btn btn-link" onClick={() => onChange(items.filter((_, j) => j !== i))} aria-label="Remove condition">✕</button>
        </div>
      ))}
      <div><button type="button" className="btn btn-light btn-small" onClick={() => onChange([...items, { source: '', operator: 'Equals', value: '' }])}>+ Condition</button></div>
    </div>
  );
}

export function Errors({ list }: { list: string[] | null | undefined }) {
  if (!list || list.length === 0) return null;
  return <div className="error" role="alert">{list.length === 1 ? list[0] : <ul className="plain">{list.map((e, i) => <li key={i}>{e}</li>)}</ul>}</div>;
}

export const errs = (x: any): string[] => x?.body?.errors ?? [x?.body?.message ?? (x?.status === 403 ? 'You do not have access to this.' : 'Something went wrong.')];
