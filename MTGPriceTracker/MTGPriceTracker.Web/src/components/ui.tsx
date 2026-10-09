import {
  forwardRef,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
} from 'react'
import { CheckCircle, Info, SpinnerGap, WarningCircle, XCircle } from '@phosphor-icons/react'

export const cx = (...parts: (string | false | null | undefined)[]) => parts.filter(Boolean).join(' ')

/* ── Buttons ─────────────────────────────────────────────── */

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md' | 'lg'

const variantClass: Record<Variant, string> = {
  primary: 'bg-accent text-accent-fg hover:brightness-110',
  secondary: 'bg-surface-2 text-fg border border-line hover:bg-[color-mix(in_srgb,var(--fg)_9%,var(--surface-2))]',
  ghost: 'text-muted hover:text-fg hover:bg-surface-2',
  danger: 'border border-up/40 text-up hover:bg-up/10',
}
const sizeClass: Record<Size, string> = {
  sm: 'h-8 px-3 text-[13px] gap-1.5',
  md: 'h-10 px-4 text-sm gap-2',
  lg: 'h-12 px-6 text-[15px] gap-2',
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  loading?: boolean
  icon?: ReactNode
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'secondary', size = 'md', loading, icon, className, children, disabled, ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      disabled={disabled || loading}
      className={cx(
        'inline-flex shrink-0 items-center justify-center whitespace-nowrap rounded-full font-medium',
        'transition duration-200 ease-out-expo active:translate-y-px active:scale-[0.98]',
        'disabled:pointer-events-none disabled:opacity-45',
        variantClass[variant],
        sizeClass[size],
        className,
      )}
      {...rest}
    >
      {loading ? <SpinnerGap className="animate-spin motion-reduce:animate-none" size={16} /> : icon}
      {children}
    </button>
  )
})

export function IconButton({
  label,
  className,
  children,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return (
    <button
      aria-label={label}
      title={label}
      className={cx(
        'inline-grid size-9 place-items-center rounded-full text-muted transition duration-200',
        'hover:bg-surface-2 hover:text-fg active:scale-95',
        className,
      )}
      {...rest}
    >
      {children}
    </button>
  )
}

/* ── Form controls ───────────────────────────────────────── */

const fieldBase =
  'h-11 w-full rounded-full border border-line bg-surface text-fg placeholder:text-muted/80 ' +
  'transition duration-200 hover:border-[color-mix(in_srgb,var(--fg)_22%,transparent)] ' +
  'focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30'

export const TextInput = forwardRef<
  HTMLInputElement,
  InputHTMLAttributes<HTMLInputElement> & { icon?: ReactNode; label?: string; wrapperClass?: string }
>(function TextInput({ icon, label, className, wrapperClass, id, ...rest }, ref) {
  const input = (
    <div className={cx('relative', wrapperClass)}>
      {icon && <span className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-muted">{icon}</span>}
      <input ref={ref} id={id} className={cx(fieldBase, icon ? 'pl-11 pr-4' : 'px-4', className)} {...rest} />
    </div>
  )
  if (!label) return input
  return (
    <label className="flex flex-col gap-2 text-sm text-muted" htmlFor={id}>
      {label}
      {input}
    </label>
  )
})

export function Select({
  label,
  className,
  children,
  ...rest
}: SelectHTMLAttributes<HTMLSelectElement> & { label?: string }) {
  const el = (
    <select className={cx(fieldBase, 'cursor-pointer appearance-none px-4 pr-9', className)} {...rest}>
      {children}
    </select>
  )
  const wrapped = (
    <div className="relative">
      {el}
      <span className="pointer-events-none absolute right-4 top-1/2 -translate-y-1/2 text-xs text-muted">▾</span>
    </div>
  )
  if (!label) return wrapped
  return (
    <label className="flex flex-col gap-2 text-sm text-muted">
      {label}
      {wrapped}
    </label>
  )
}

/** Pill-shaped single-choice control. */
export function Segmented<T extends string | number>({
  value,
  onChange,
  options,
  label,
}: {
  value: T
  onChange: (v: T) => void
  options: { value: T; label: ReactNode }[]
  label: string
}) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="inline-flex rounded-full border border-line bg-surface p-1"
    >
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={String(o.value)}
            role="radio"
            aria-checked={active}
            onClick={() => onChange(o.value)}
            className={cx(
              'h-8 rounded-full px-3.5 text-[13px] font-medium transition duration-200 ease-out-expo',
              active ? 'bg-fg text-bg' : 'text-muted hover:text-fg',
            )}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

export function Switch({
  checked,
  onChange,
  label,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label: string
}) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className="group inline-flex items-center gap-2.5 text-sm text-muted transition hover:text-fg"
    >
      <span
        className={cx(
          'relative h-6 w-10 rounded-full border transition duration-200',
          checked ? 'border-accent bg-accent' : 'border-line bg-surface-2',
        )}
      >
        <span
          className={cx(
            'absolute top-0.5 size-4.5 rounded-full transition-transform duration-200 ease-out-expo',
            checked ? 'translate-x-[17px] bg-accent-fg' : 'translate-x-0.5 bg-muted',
          )}
        />
      </span>
      {label}
    </button>
  )
}

/* ── Feedback ────────────────────────────────────────────── */

type Tone = 'info' | 'success' | 'warning' | 'error'
const toneIcon = {
  info: <Info size={18} />,
  success: <CheckCircle size={18} />,
  warning: <WarningCircle size={18} />,
  error: <XCircle size={18} />,
}
const toneClass: Record<Tone, string> = {
  info: 'border-line bg-surface text-fg [&>svg]:text-muted',
  success: 'border-down/30 bg-down/10 text-fg [&>svg]:text-down',
  warning: 'border-accent/35 bg-accent/10 text-fg [&>svg]:text-accent',
  error: 'border-up/35 bg-up/10 text-fg [&>svg]:text-up',
}

export function Alert({ tone = 'info', children, className }: { tone?: Tone; children: ReactNode; className?: string }) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={cx('flex items-start gap-3 rounded-2xl border px-4 py-3 text-sm', toneClass[tone], className)}
    >
      <span className="mt-0.5 shrink-0">{toneIcon[tone]}</span>
      <div className="min-w-0 flex-1 leading-relaxed">{children}</div>
    </div>
  )
}

export const Skeleton = ({ className }: { className?: string }) => (
  <div aria-hidden className={cx('skeleton rounded-xl', className)} />
)

export function Spinner({ className }: { className?: string }) {
  return (
    <SpinnerGap
      aria-label="Loading"
      className={cx('animate-spin text-muted motion-reduce:animate-none', className)}
      size={22}
    />
  )
}

export function EmptyState({
  icon,
  title,
  children,
  action,
}: {
  icon: ReactNode
  title: string
  children?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-3 py-20 text-center">
      <div className="grid size-14 place-items-center rounded-2xl bg-surface-2 text-muted">{icon}</div>
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      {children && <p className="text-sm leading-relaxed text-muted">{children}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

/* ── Layout helpers ──────────────────────────────────────── */

export const Panel = ({ className, children }: { className?: string; children: ReactNode }) => (
  <div className={cx('rounded-[var(--radius-panel)] border border-line bg-surface', className)}>{children}</div>
)

export const Badge = ({ children, className }: { children: ReactNode; className?: string }) => (
  <span
    className={cx(
      'inline-flex items-center rounded-full border border-line bg-surface-2 px-2.5 py-0.5 text-xs font-medium',
      className,
    )}
  >
    {children}
  </span>
)
