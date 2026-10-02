'use client';

import {
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
  useEffect,
  useId,
  useRef,
} from 'react';

/** Shared interface primitives, so screens describe intent rather than repeat styling. */

type ButtonVariant = 'primary' | 'secondary' | 'quiet' | 'danger';

const BUTTON_STYLES: Record<ButtonVariant, string> = {
  primary: 'bg-ink text-surface-raised border-ink hover:bg-ink/90',
  secondary: 'bg-surface-raised text-ink border-line-strong hover:bg-surface-sunken',
  quiet: 'bg-transparent text-accent-ink border-transparent hover:bg-accent-soft px-2',
  danger: 'bg-transparent text-danger border-danger/40 hover:bg-danger-soft',
};

export function Button({
  variant = 'primary',
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }) {
  return (
    <button
      {...props}
      className={`inline-flex items-center justify-center gap-2 rounded-xl border px-4 py-2 font-semibold transition-colors disabled:opacity-55 ${BUTTON_STYLES[variant]} ${className}`}
    />
  );
}

export function Card({
  children,
  className = '',
  as: Element = 'section',
}: {
  children: ReactNode;
  className?: string;
  as?: 'section' | 'article' | 'div' | 'li';
}) {
  return (
    <Element className={`rounded-2xl border border-line bg-surface-raised p-5 ${className}`}>
      {children}
    </Element>
  );
}

/**
 * A labelled form control.
 *
 * The label is always a real `<label>` bound to the control, and the hint is wired
 * through `aria-describedby`, so a screen reader announces both.
 */
export function Field({
  label,
  hint,
  error,
  children,
  optional,
  className = '',
}: {
  label: string;
  hint?: string;
  error?: string;
  children: (ids: { id: string; describedBy?: string }) => ReactNode;
  optional?: string;
  className?: string;
}) {
  const id = useId();
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined;

  return (
    <div className={`flex flex-col gap-1.5 ${className}`}>
      <label htmlFor={id} className="text-sm font-semibold">
        {label}
        {optional ? <span className="ml-1 font-normal text-ink-faint">({optional})</span> : null}
      </label>
      {children({ id, describedBy })}
      {hint ? (
        <p id={hintId} className="text-sm text-ink-muted">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} className="text-sm font-semibold text-danger">
          {error}
        </p>
      ) : null}
    </div>
  );
}

const CONTROL =
  'w-full rounded-lg border border-line-strong bg-surface-raised px-3 py-2 placeholder:text-ink-faint';

export function Input({ className = '', ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`${CONTROL} ${className}`} />;
}

export function Select({ className = '', ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className={`${CONTROL} ${className}`} />;
}

export function Textarea({ className = '', ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={`${CONTROL} ${className}`} />;
}

type BadgeTone = 'neutral' | 'accent' | 'positive' | 'warning' | 'danger' | 'quiet';

const BADGE_STYLES: Record<BadgeTone, string> = {
  neutral: 'bg-surface-sunken text-ink',
  accent: 'bg-accent-soft text-accent-ink',
  positive: 'bg-positive-soft text-positive',
  warning: 'bg-warning-soft text-warning',
  danger: 'bg-danger-soft text-danger',
  quiet: 'bg-transparent text-ink-faint border border-line',
};

export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-bold ${BADGE_STYLES[tone]}`}
    >
      {children}
    </span>
  );
}

export function Notice({
  tone = 'warning',
  children,
}: {
  tone?: 'warning' | 'danger' | 'positive';
  children: ReactNode;
}) {
  const styles = {
    warning: 'bg-warning-soft text-warning',
    danger: 'bg-danger-soft text-danger',
    positive: 'bg-positive-soft text-positive',
  }[tone];

  // Announced to assistive technology without stealing focus.
  return (
    <p role="status" className={`rounded-xl px-3 py-2 text-sm font-semibold ${styles}`}>
      {children}
    </p>
  );
}

export function EmptyState({ title, hint, action }: { title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="rounded-2xl border border-dashed border-line-strong px-6 py-12 text-center">
      <p className="font-semibold">{title}</p>
      {hint ? <p className="mt-1 text-sm text-ink-muted">{hint}</p> : null}
      {action ? <div className="mt-4 flex justify-center">{action}</div> : null}
    </div>
  );
}

/**
 * A modal dialog that traps nothing and relies on the platform.
 *
 * Uses the native `<dialog>` element so Escape, the backdrop and focus handling come
 * from the browser rather than from hand-written key listeners.
 */
export function Dialog({
  open,
  onClose,
  title,
  children,
  footer,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) {
      return;
    }

    if (open && !element.open) {
      element.showModal();
    } else if (!open && element.open) {
      element.close();
    }
  }, [open]);

  return (
    <dialog
      ref={ref}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        // Clicking the backdrop, which is the dialog element itself, dismisses it.
        if (event.target === ref.current) {
          onClose();
        }
      }}
      className="m-auto w-[min(34rem,calc(100vw-2rem))] rounded-2xl border border-line bg-surface-raised p-0 text-ink backdrop:bg-ink/40"
    >
      <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
        <h2 className="text-lg font-bold">{title}</h2>
        <Button variant="quiet" onClick={onClose} aria-label={title}>
          ✕
        </Button>
      </div>
      <div className="max-h-[70vh] overflow-y-auto px-5 py-4">{children}</div>
      {footer ? (
        <div className="flex flex-wrap justify-end gap-2 border-t border-line px-5 py-4">{footer}</div>
      ) : null}
    </dialog>
  );
}

/** A disclosure for advanced controls, so the default surface stays uncluttered. */
export function Advanced({ label, children }: { label: string; children: ReactNode }) {
  return (
    <details className="rounded-xl border border-line bg-surface-sunken/50">
      <summary className="cursor-pointer px-3 py-2 text-sm font-semibold">{label}</summary>
      <div className="border-t border-line px-3 py-3">{children}</div>
    </details>
  );
}

export function Spinner({ label }: { label: string }) {
  return (
    <p role="status" className="py-12 text-center text-ink-muted">
      {label}
    </p>
  );
}
