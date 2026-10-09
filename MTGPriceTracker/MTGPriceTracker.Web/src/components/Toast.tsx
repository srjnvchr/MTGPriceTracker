import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { X } from '@phosphor-icons/react'
import { Alert } from './ui'

type Severity = 'success' | 'info' | 'warning' | 'error'
type ToastFn = (message: string, severity?: Severity) => void

const ToastContext = createContext<ToastFn>(() => {})

// eslint-disable-next-line react-refresh/only-export-components
export const useToast = () => useContext(ToastContext)

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<{ message: string; severity: Severity; key: number } | null>(null)

  const show = useCallback<ToastFn>((message, severity = 'info') => {
    setToast({ message, severity, key: Date.now() })
  }, [])

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 4000)
    return () => clearTimeout(t)
  }, [toast])

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div className="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end">
        <AnimatePresence>
          {toast && (
            <motion.div
              key={toast.key}
              initial={{ opacity: 0, y: 16, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: 8 }}
              transition={{ type: 'spring', stiffness: 260, damping: 26 }}
              className="pointer-events-auto flex max-w-sm items-start gap-2 rounded-2xl bg-surface shadow-2xl shadow-black/30"
            >
              <Alert tone={toast.severity} className="flex-1">
                {toast.message}
              </Alert>
              <button
                aria-label="Dismiss"
                onClick={() => setToast(null)}
                className="mr-2 mt-3 text-muted transition hover:text-fg"
              >
                <X size={16} />
              </button>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </ToastContext.Provider>
  )
}
