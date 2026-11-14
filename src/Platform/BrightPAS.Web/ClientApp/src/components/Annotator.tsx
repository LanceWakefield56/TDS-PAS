import { useEffect, useRef, useState } from 'react';

type Pt = { x: number; y: number };
type Shape = { tool: 'pen' | 'arrow' | 'circle' | 'text'; color: string; pts: Pt[]; text?: string };
const COLOURS: [string, string][] = [['#FFEB3B', 'Yellow'], ['#00E676', 'Green'], ['#FFFFFF', 'White'], ['#2979FF', 'Blue'], ['#000000', 'Black']];

/**
 * Draw on a copy of an endoscopy picture: pen, arrow, circle and text labels. The original picture is never changed;
 * "Save" sends a PNG of the drawn copy, which BrightPAS keeps as a new picture linked to the original.
 */
export default function Annotator({ src, onSave, onCancel }: { src: string; onSave: (png: Blob) => Promise<void>; onCancel: () => void }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const img = useRef<HTMLImageElement | null>(null);
  const [tool, setTool] = useState<Shape['tool']>('arrow');
  const [color, setColor] = useState(COLOURS[0][0]);
  const [shapes, setShapes] = useState<Shape[]>([]);
  const [draft, setDraft] = useState<Shape | null>(null);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const i = new Image();
    i.onload = () => { img.current = i; const c = canvas.current!; c.width = i.naturalWidth; c.height = i.naturalHeight; draw([]); };
    i.onerror = () => setError('The picture could not be loaded.');
    i.src = src;
  }, [src]); // eslint-disable-line react-hooks/exhaustive-deps

  const lw = () => Math.max(3, Math.round((canvas.current?.width ?? 800) / 200));
  const draw = (list: Shape[]) => {
    const c = canvas.current; const i = img.current;
    if (!c || !i) return;
    const g = c.getContext('2d')!;
    g.drawImage(i, 0, 0);
    for (const s of list) {
      g.strokeStyle = s.color; g.fillStyle = s.color; g.lineWidth = lw(); g.lineCap = 'round'; g.lineJoin = 'round';
      g.shadowColor = 'rgba(0,0,0,0.6)'; g.shadowBlur = lw();
      const [a, b] = [s.pts[0], s.pts[s.pts.length - 1]];
      if (s.tool === 'pen') { g.beginPath(); s.pts.forEach((p, k) => k ? g.lineTo(p.x, p.y) : g.moveTo(p.x, p.y)); g.stroke(); }
      else if (s.tool === 'circle') { g.beginPath(); g.ellipse((a.x + b.x) / 2, (a.y + b.y) / 2, Math.abs(b.x - a.x) / 2 || 1, Math.abs(b.y - a.y) / 2 || 1, 0, 0, Math.PI * 2); g.stroke(); }
      else if (s.tool === 'arrow') {
        g.beginPath(); g.moveTo(a.x, a.y); g.lineTo(b.x, b.y); g.stroke();
        const ang = Math.atan2(b.y - a.y, b.x - a.x), head = lw() * 5;
        g.beginPath(); g.moveTo(b.x, b.y); g.lineTo(b.x - head * Math.cos(ang - 0.45), b.y - head * Math.sin(ang - 0.45)); g.lineTo(b.x - head * Math.cos(ang + 0.45), b.y - head * Math.sin(ang + 0.45)); g.closePath(); g.fill();
      } else if (s.tool === 'text' && s.text) { g.font = `bold ${lw() * 8}px Arial, sans-serif`; g.textBaseline = 'middle'; g.fillText(s.text, a.x, a.y); }
    }
    g.shadowBlur = 0;
  };
  useEffect(() => { draw(draft ? [...shapes, draft] : shapes); }, [shapes, draft]); // eslint-disable-line react-hooks/exhaustive-deps

  const pos = (e: React.PointerEvent): Pt => { const c = canvas.current!; const r = c.getBoundingClientRect(); return { x: (e.clientX - r.left) * c.width / r.width, y: (e.clientY - r.top) * c.height / r.height }; };
  const down = (e: React.PointerEvent) => {
    const p = pos(e);
    if (tool === 'text') { if (text.trim()) setShapes([...shapes, { tool, color, pts: [p], text: text.trim() }]); else setError('Type the label first, then click where it goes.'); return; }
    (e.target as Element).setPointerCapture(e.pointerId);
    setDraft({ tool, color, pts: [p, p] });
  };
  const move = (e: React.PointerEvent) => { if (!draft) return; const p = pos(e); setDraft({ ...draft, pts: draft.tool === 'pen' ? [...draft.pts, p] : [draft.pts[0], p] }); };
  const up = () => { if (draft) { setShapes([...shapes, draft]); setDraft(null); } };
  const save = async () => {
    setBusy(true); setError(null);
    try { const blob = await new Promise<Blob | null>((res) => canvas.current!.toBlob(res, 'image/png')); if (!blob) throw new Error('Could not make the picture.'); await onSave(blob); }
    catch (e: any) { setError(e?.message ?? 'Not saved.'); } finally { setBusy(false); }
  };
  return (
    <div className="annotator" role="dialog" aria-label="Draw on picture">
      <div className="annotator-panel stack">
        <div className="row">
          {(['arrow', 'circle', 'pen', 'text'] as const).map((t) => <button key={t} className={tool === t ? 'btn btn-primary btn-small' : 'btn btn-light btn-small'} aria-pressed={tool === t} onClick={() => setTool(t)}>{{ arrow: 'Arrow', circle: 'Circle', pen: 'Pen', text: 'Text' }[t]}</button>)}
          {tool === 'text' && <input aria-label="Label text" value={text} onChange={(e) => setText(e.target.value)} placeholder="Label, e.g. polyp 1" />}
          <span className="row">{COLOURS.map(([c, n]) => <button key={c} className={`swatch${color === c ? ' on' : ''} swatch-${n.toLowerCase()}`} aria-label={n} aria-pressed={color === c} onClick={() => setColor(c)} />)}</span>
          <button className="btn btn-light btn-small" disabled={shapes.length === 0} onClick={() => setShapes(shapes.slice(0, -1))}>Undo</button>
        </div>
        {error && <p className="error-text" role="alert">{error}</p>}
        <canvas ref={canvas} className="annotator-canvas" onPointerDown={down} onPointerMove={move} onPointerUp={up} aria-label="Picture to draw on" />
        <div className="row"><button className="btn btn-primary" disabled={busy || shapes.length === 0} onClick={save}>Save annotated copy</button><button className="btn btn-light" onClick={onCancel}>Cancel</button>
          <span className="small muted">The original picture is kept unchanged.</span></div>
      </div>
    </div>
  );
}
