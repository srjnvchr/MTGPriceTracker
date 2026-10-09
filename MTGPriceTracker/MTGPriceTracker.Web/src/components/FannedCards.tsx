import { useNavigate } from 'react-router-dom'
import { motion, useMotionValue, useReducedMotion, useSpring, useTransform, type MotionValue } from 'motion/react'
import type { CardDto } from '../api/types'
import TiltCard from './TiltCard'

function FanCard({
  card,
  index,
  total,
  px,
}: {
  card: CardDto
  index: number
  total: number
  px: MotionValue<number>
}) {
  const navigate = useNavigate()
  const reduce = useReducedMotion()
  const offset = index - (total - 1) / 2
  const depth = total - Math.abs(offset)
  // Cards further from the centre drift more with the pointer, giving parallax depth.
  const x = useTransform(px, [0, 1], [-offset * 10 - 14, -offset * 10 + 14])

  return (
    <motion.div
      className="absolute cursor-pointer"
      style={{
        left: `calc(50% - 21% + ${offset * 12.5}%)`,
        top: `${Math.abs(offset) * 3.5}%`,
        width: '42%',
        zIndex: Math.round(depth * 10),
        transformOrigin: '50% 100%',
        x: reduce ? 0 : x,
      }}
      initial={reduce ? false : { opacity: 0, y: 60, rotate: 0 }}
      animate={{ opacity: 1, y: 0, rotate: offset * 7 }}
      transition={{ type: 'spring', stiffness: 90, damping: 18, delay: 0.15 + index * 0.07 }}
      whileHover={reduce ? undefined : { y: -16, zIndex: 50 }}
      onClick={() => navigate(`/cards/${card.uuid}`)}
    >
      <TiltCard card={card} max={9} priority />
    </motion.div>
  )
}

/** A fanned hand of real cards from the user's catalog. */
export default function FannedCards({ cards }: { cards: CardDto[] }) {
  const px = useMotionValue(0.5)
  const spring = useSpring(px, { stiffness: 80, damping: 20 })

  return (
    <div
      className="relative mx-auto aspect-[5/4.4] w-[88%] max-w-[560px] sm:w-full"
      onPointerMove={(e) => {
        const r = e.currentTarget.getBoundingClientRect()
        px.set((e.clientX - r.left) / r.width)
      }}
      onPointerLeave={() => px.set(0.5)}
    >
      {cards.map((c, i) => (
        <FanCard key={c.uuid} card={c} index={i} total={cards.length} px={spring} />
      ))}
    </div>
  )
}
