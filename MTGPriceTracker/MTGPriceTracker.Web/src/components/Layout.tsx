import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { Bug, CardsThree, CaretDown, List, Moon, Sun, X } from '@phosphor-icons/react'
import { useTheme } from '../lib/theme'
import { cx, IconButton } from './ui'

const mainLinks = [
  { to: '/', label: 'Home', end: true },
  { to: '/cards', label: 'All Cards' },
  { to: '/favorites', label: 'Favorites' },
]
const manageLinks = [
  { to: '/sync', label: 'Data Sync' },
  { to: '/debug/goodgames', label: 'GG Inspector', icon: <Bug size={16} /> },
]

const linkClass = ({ isActive }: { isActive: boolean }) =>
  cx(
    'rounded-full px-3.5 py-1.5 text-sm font-medium transition duration-200',
    isActive ? 'bg-surface-2 text-fg' : 'text-muted hover:text-fg',
  )

function ManageMenu() {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const { pathname } = useLocation()
  const active = manageLinks.some((l) => pathname.startsWith(l.to))

  useEffect(() => setOpen(false), [pathname])
  useEffect(() => {
    if (!open) return
    const onDown = (e: PointerEvent) => !ref.current?.contains(e.target as Node) && setOpen(false)
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('pointerdown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('pointerdown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={cx(
          'inline-flex items-center gap-1 rounded-full px-3.5 py-1.5 text-sm font-medium transition duration-200',
          active || open ? 'bg-surface-2 text-fg' : 'text-muted hover:text-fg',
        )}
      >
        Manage
        <CaretDown size={14} className={cx('transition-transform duration-200', open && 'rotate-180')} />
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            role="menu"
            initial={{ opacity: 0, y: -6, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -4 }}
            transition={{ duration: 0.16 }}
            className="lift absolute right-0 top-full z-40 mt-2 w-52 rounded-2xl border border-line bg-surface p-1.5"
          >
            {manageLinks.map((l) => (
              <NavLink
                key={l.to}
                to={l.to}
                role="menuitem"
                className={({ isActive }) =>
                  cx(
                    'flex items-center gap-2 rounded-xl px-3 py-2 text-sm transition',
                    isActive ? 'bg-surface-2 text-fg' : 'text-muted hover:bg-surface-2 hover:text-fg',
                  )
                }
              >
                {l.icon}
                {l.label}
              </NavLink>
            ))}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

export default function Layout({ children }: { children: ReactNode }) {
  const { resolved, toggle } = useTheme()
  const [mobileOpen, setMobileOpen] = useState(false)
  const { pathname } = useLocation()

  useEffect(() => setMobileOpen(false), [pathname])

  return (
    <div className="min-h-dvh">
      <header className="sticky top-0 z-30 border-b border-line bg-bg/80 backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-[1400px] items-center gap-3 px-4 sm:px-6">
          <Link to="/" className="mr-2 flex items-center gap-2.5 font-semibold tracking-tight">
            <span className="grid size-8 place-items-center rounded-xl bg-accent text-accent-fg">
              <CardsThree size={19} weight="fill" />
            </span>
            <span className="hidden sm:inline">MTG Price Tracker</span>
          </Link>

          <nav className="hidden items-center gap-1 md:flex" aria-label="Primary">
            {mainLinks.map((l) => (
              <NavLink key={l.to} to={l.to} end={l.end} className={linkClass}>
                {l.label}
              </NavLink>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-1">
            <div className="hidden md:block">
              <ManageMenu />
            </div>
            <IconButton label={resolved === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'} onClick={toggle}>
              {resolved === 'dark' ? <Sun size={19} /> : <Moon size={19} />}
            </IconButton>
            <IconButton
              label={mobileOpen ? 'Close menu' : 'Open menu'}
              className="md:hidden"
              onClick={() => setMobileOpen((o) => !o)}
            >
              {mobileOpen ? <X size={20} /> : <List size={20} />}
            </IconButton>
          </div>
        </div>

        <AnimatePresence>
          {mobileOpen && (
            <motion.nav
              aria-label="Mobile"
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.22, ease: [0.16, 1, 0.3, 1] }}
              className="overflow-hidden border-t border-line md:hidden"
            >
              <div className="flex flex-col gap-1 p-3">
                {[...mainLinks, ...manageLinks].map((l) => (
                  <NavLink
                    key={l.to}
                    to={l.to}
                    end={'end' in l ? l.end : undefined}
                    className={({ isActive }) =>
                      cx(
                        'rounded-xl px-4 py-3 text-[15px] font-medium',
                        isActive ? 'bg-surface-2 text-fg' : 'text-muted',
                      )
                    }
                  >
                    {l.label}
                  </NavLink>
                ))}
              </div>
            </motion.nav>
          )}
        </AnimatePresence>
      </header>

      <main className="mx-auto max-w-[1400px] px-4 pb-24 pt-8 sm:px-6">{children}</main>
    </div>
  )
}
