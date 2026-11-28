import { ReactNode, useEffect, useId, useRef } from 'react';

/**
 * A pop-up window for edit and add forms, so pressing "Edit" always shows the form straight away
 * (it used to open further down the page, out of sight). Uses the browser's own dialog: Esc or the
 * close button cancels, the page behind is dimmed and cannot be clicked until it is closed.
 */
export default function Modal({ title, onClose, children, wide }: { title: ReactNode; onClose: () => void; children: ReactNode; wide?: boolean }) {
  const ref = useRef<HTMLDialogElement>(null);
  const close = useRef(onClose);
  close.current = onClose;
  const id = useId();
  useEffect(() => {
    const d = ref.current;
    if (d && !d.open) d.showModal();
    const cancel = (e: Event) => { e.preventDefault(); close.current(); };
    d?.addEventListener('cancel', cancel);
    return () => { d?.removeEventListener('cancel', cancel); if (d?.open) d.close(); };
  }, []);
  return (
    <dialog ref={ref} className={`modal${wide ? ' wide' : ''}`} aria-labelledby={id}>
      <div className="modal-head">
        <h2 id={id}>{title}</h2>
        <button type="button" className="modal-close" aria-label="Close" onClick={() => close.current()}><span aria-hidden="true">×</span></button>
      </div>
      <div className="modal-body stack">{children}</div>
    </dialog>
  );
}

/**
 * For an "add" form that is always on the page and that "Edit" fills in: shown in place while adding,
 * and in a pop-up while editing so the form is in view whichever row was pressed.
 */
export function EditInPopup({ editing, title, onClose, className, children }: { editing: boolean; title: ReactNode; onClose: () => void; className: string; children: ReactNode }) {
  return editing ? <Modal title={title} onClose={onClose}><div className={className}>{children}</div></Modal> : <div className={className}>{children}</div>;
}
