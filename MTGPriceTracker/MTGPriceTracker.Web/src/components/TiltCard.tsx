import { useRef, type CSSProperties, type ReactNode } from 'react'
import {
  motion,
  useMotionTemplate,
  useMotionValue,
  useReducedMotion,
  useSpring,
  useTransform,
} from 'motion/react'
import { Cards } from '@phosphor-icons/react'
import type { CardDto } from '../api/types'
import { scryfallImage } from '../lib/format'

/**
 * Card artwork with pointer-driven 3D tilt and a moving glare.
 * Pointer position lives in motion values, never React state, so hovering
 * does not re-render anything. Tilt is disabled under reduced motion.
 */
export default function TiltCard({
  card,
  max = 10,
  className,
  style,
  children,
  priority,
  size = 'normal',
}: {
  card: Pick<CardDto, 'scryfallId' | 'name' | 'hasFoil'>
  max?: number
  className?: string
  style?: CSSProperties
  children?: ReactNode
  priority?: boolean
  size?: 'small' | 'normal' | 'large'
}) {
  const reduce = useReducedMotion()
  const ref = useRef<HTMLDivElement>(null)

  const px = useMotionValue(0.5)
  const py = useMotionValue(0.5)
  const sx = useSpring(px, { stiffness: 220, damping: 22 })
  const sy = useSpring(py, { stiffness: 220, damping: 22 })

  const rotateY = useTransform(sx, [0, 1], [-max, max])
  const rotateX = useTransform(sy, [0, 1], [max, -max])
  const gx = useTransform(sx, [0, 1], [0, 100])
  const gy = useTransform(sy, [0, 1], [0, 100])
  const glare = useMotionTemplate`radial-gradient(circle at ${gx}% ${gy}%, rgb(255 255 255 / 0.42), transparent 55%)`

  const onMove = (e: React.PointerEvent) => {
    if (reduce || !ref.current) return
    const r = ref.current.getBoundingClientRect()
    px.set((e.clientX - r.left) / r.width)
    py.set((e.clientY - r.top) / r.height)
  }
  const onLeave = () => {
    px.set(0.5)
    py.set(0.5)
  }

  const image = scryfallImage(card)
  const src = size === 'small' ? image?.replace('/normal/', '/small/') : image

  return (
    <div style={{ perspective: 900 }} className={className}>
      <motion.div
        ref={ref}
        onPointerMove={onMove}
        onPointerLeave={onLeave}
        style={reduce ? style : { ...style, rotateX, rotateY }}
        className="tilt card-art lift h-full w-full"
      >
        {src ? (
          <img
            className="card-art__img"
            src={src}
            alt={card.name}
            loading={priority ? 'eager' : 'lazy'}
            decoding="async"
            draggable={false}
          />
        ) : (
          <div className="card-art__placeholder">
            <Cards size={40} />
          </div>
        )}
        {card.hasFoil ? <div className="card-art__foil" aria-hidden /> : <div className="card-art__matte" aria-hidden />}
        {!reduce && <motion.div className="card-art__glare" style={{ background: glare }} aria-hidden />}
        {children}
      </motion.div>
    </div>
  )
}
