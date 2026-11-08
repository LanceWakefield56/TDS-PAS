import { useEffect, useState } from 'react';
import { useParams, useSearchParams } from 'react-router';
import { api, errText, formatDate } from '../api';

/** Patient labels for a DYMO LabelWriter (or any Windows label printer): sized to the label stock and printed from the browser. */
export default function DymoLabels() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const [sheet, setSheet] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    api(`/api/patients/${id}/print/dymo`, { body: { kind: 'Label', copies: Number(params.get('copies') ?? 1), printerId: params.get('printer') } })
      .then((s) => { setSheet(s); setTimeout(() => window.print(), 300); }).catch((e) => setError(errText(e)));
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps
  if (error) return <div className="card"><p className="error">{error}</p></div>;
  if (!sheet) return <p>Preparing labels…</p>;
  const b = sheet.barcode;
  const label = (i: number) => (
    <div key={i} className={`dymo-label media-${sheet.media}`}>
      <div className="dymo-name">{sheet.name}</div>
      <div className="dymo-line">DOB {formatDate(sheet.dateOfBirth)} · {sheet.sex}{sheet.allergy && <strong className="dymo-allergy"> ALLERGY</strong>}</div>
      <div className="dymo-line">NHI {sheet.nhi ?? '—'} · {sheet.mrn}</div>
      <svg className="dymo-barcode" viewBox={`0 0 ${b.width} 30`} preserveAspectRatio="none" role="img" aria-label={`Barcode ${b.text}`}>
        {b.bars.map(([x, w]: number[], j: number) => <rect key={j} x={x} y={0} width={w} height={30} />)}
      </svg>
    </div>
  );
  return (
    <div className="dymo-sheet">
      <div className="no-print dymo-help"><p>Printing {sheet.copies} label(s) on <strong>{sheet.printer}</strong> (DYMO {sheet.media}). In the print window choose the DYMO printer, margins <strong>None</strong>, scale 100 %.</p>
        <button className="btn btn-primary" onClick={() => window.print()}>Print again</button> <button className="btn btn-light" onClick={() => window.history.back()}>Back</button></div>
      {Array.from({ length: sheet.copies }, (_, i) => label(i))}
    </div>
  );
}
