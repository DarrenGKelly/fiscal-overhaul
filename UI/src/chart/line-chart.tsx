import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import {
  formatMoney,
  formatMoneyShort,
  formatMonthsAgo,
  formatMonthsAxis,
  nearestIndex,
  nearestSeries,
  niceTicks,
  scale,
  valueRange,
} from "./chart-math";
import styles from "./line-chart.module.scss";

export interface ChartLine {
  key: string;
  label: string;
  color: string;
  values: number[];
  /** Shown negated in the tooltip (expenses are plotted as positive amounts). */
  negateInTooltip?: boolean;
}

interface Props {
  lines: ChartLine[];
  monthsAgo: number[];
  highlightKey: string | null;
  onHoverLine: (key: string | null) => void;
}

// Base sizes in rem. CS2 lays its UI out for 4K, so 1rem is about 0.67 px at 1440p; canvas pixels must be scaled by it.
const PAD_REM = { left: 130, right: 24, top: 20, bottom: 48 };
const FONT_REM = 22;

/** Pixels per rem, measured from a 100rem-wide probe (Gameface's computed root font size isn't reliable). */
function measureRem(probe: HTMLDivElement | null): number {
  const rem = probe ? probe.offsetWidth / 100 : 0;
  return rem > 0.25 && rem < 8 ? rem : 1;
}

interface Hover {
  x: number;
  y: number;
  index: number;
  key: string;
}

function scalePad(rem: number) {
  return { left: PAD_REM.left * rem, right: PAD_REM.right * rem, top: PAD_REM.top * rem, bottom: PAD_REM.bottom * rem };
}

/** Drawn on a canvas: the game's UI (Coherent Gameface) supports 2D canvas well; inline SVG it does not. */
export const LineChart = ({ lines, monthsAgo, highlightKey, onHoverLine }: Props) => {
  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const probeRef = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0, rem: 1 });
  const [hover, setHover] = useState<Hover | null>(null);

  // No ResizeObserver in Gameface: measure after layout and poll cheaply for panel resizes.
  const measure = useCallback(() => {
    const el = containerRef.current;
    if (!el) return;
    const w = el.clientWidth;
    const h = el.clientHeight;
    const rem = measureRem(probeRef.current);
    setSize((s) => (s.w === w && s.h === h && s.rem === rem ? s : { w, h, rem }));
  }, []);
  useLayoutEffect(measure);
  useEffect(() => {
    const id = window.setInterval(measure, 500);
    return () => window.clearInterval(id);
  }, [measure]);

  const geometry = useMemo(() => {
    const PAD = scalePad(size.rem);
    const [vMin, vMax] = valueRange(lines);
    const ticks = niceTicks(vMin, vMax, 5);
    const yMin = Math.min(vMin, ticks[0] ?? vMin);
    const yMax = Math.max(vMax, ticks[ticks.length - 1] ?? vMax);
    const xFirst = monthsAgo.length > 0 ? monthsAgo[0] : -1;
    const xLast = monthsAgo.length > 0 ? monthsAgo[monthsAgo.length - 1] : 0;
    const x = scale(xFirst, xLast === xFirst ? xFirst + 1 : xLast, PAD.left, Math.max(PAD.left + 1, size.w - PAD.right));
    const y = scale(yMin, yMax, Math.max(PAD.top + 1, size.h - PAD.bottom), PAD.top);
    const px = monthsAgo.map(x);
    return { ticks, x, y, px, xFirst, xLast, PAD };
  }, [lines, monthsAgo, size]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || size.w <= 0 || size.h <= 0) return;
    canvas.width = size.w;
    canvas.height = size.h;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.clearRect(0, 0, size.w, size.h);

    const { ticks, y, px, xFirst, xLast, x, PAD } = geometry;
    const rem = size.rem;

    // Horizontal grid + y labels.
    ctx.font = `${Math.round(FONT_REM * rem)}px Overpass, sans-serif`;
    ctx.textBaseline = "middle";
    ctx.textAlign = "right";
    for (const t of ticks) {
      const ty = Math.round(y(t)) + 0.5;
      ctx.strokeStyle = t === 0 ? "rgba(255,255,255,0.5)" : "rgba(255,255,255,0.16)";
      ctx.lineWidth = Math.max(1, Math.round(1.5 * rem));
      ctx.beginPath();
      ctx.moveTo(PAD.left, ty);
      ctx.lineTo(size.w - PAD.right, ty);
      ctx.stroke();
      ctx.fillStyle = "rgba(255,255,255,0.8)";
      ctx.fillText(formatMoneyShort(t), PAD.left - 12 * rem, ty);
    }

    // X labels: start, middle, end.
    ctx.textAlign = "center";
    ctx.textBaseline = "top";
    ctx.fillStyle = "rgba(255,255,255,0.8)";
    const xLabels = [xFirst, (xFirst + xLast) / 2, xLast];
    xLabels.forEach((m, i) => {
      ctx.textAlign = i === 0 ? "left" : i === 2 ? "right" : "center";
      ctx.fillText(formatMonthsAxis(m), x(m), size.h - PAD.bottom + 12 * rem);
    });

    // Lines: highlighted one last and thicker.
    const ordered = [...lines].sort((a, b) => (a.key === highlightKey ? 1 : 0) - (b.key === highlightKey ? 1 : 0));
    for (const line of ordered) {
      if (line.values.length === 0) continue;
      const highlighted = line.key === highlightKey;
      ctx.strokeStyle = line.color;
      ctx.globalAlpha = highlightKey && !highlighted ? 0.55 : 1;
      ctx.lineWidth = Math.max(2, (highlighted ? 6 : 4) * rem);
      ctx.lineJoin = "round";
      ctx.beginPath();
      line.values.forEach((v, i) => {
        const lx = px[i];
        const ly = y(v);
        if (i === 0) ctx.moveTo(lx, ly);
        else ctx.lineTo(lx, ly);
      });
      ctx.stroke();
    }
    ctx.globalAlpha = 1;

    // Hover marker.
    if (hover) {
      const line = lines.find((l) => l.key === hover.key);
      const v = line?.values[hover.index];
      if (line && v !== undefined) {
        const hx = px[hover.index];
        ctx.strokeStyle = "rgba(255,255,255,0.45)";
        ctx.lineWidth = Math.max(1, Math.round(1.5 * rem));
        ctx.beginPath();
        ctx.moveTo(Math.round(hx) + 0.5, PAD.top);
        ctx.lineTo(Math.round(hx) + 0.5, size.h - PAD.bottom);
        ctx.stroke();
        ctx.fillStyle = line.color;
        ctx.beginPath();
        ctx.arc(hx, y(v), Math.max(4, 9 * rem), 0, Math.PI * 2);
        ctx.fill();
      }
    }
  }, [geometry, lines, highlightKey, hover, size]);

  const onMouseMove = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      const el = containerRef.current;
      if (!el || lines.length === 0) return;
      const rect = el.getBoundingClientRect();
      const mx = e.clientX - rect.left;
      const my = e.clientY - rect.top;
      const index = nearestIndex(geometry.px, mx);
      if (index < 0) return;
      // Cursor height back to a value, then the line closest to it at this x.
      const yToValue = scale(geometry.y(0), geometry.y(1), 0, 1);
      const value = yToValue(my);
      const line = nearestSeries(lines, index, value);
      if (!line) return;
      setHover({ x: mx, y: my, index, key: line.key });
      onHoverLine(line.key);
    },
    [geometry, lines, onHoverLine]
  );

  const onMouseLeave = useCallback(() => {
    setHover(null);
    onHoverLine(null);
  }, [onHoverLine]);

  const hoverLine = hover ? lines.find((l) => l.key === hover.key) : undefined;
  const hoverValue = hover && hoverLine ? hoverLine.values[hover.index] : undefined;
  const tooltipLeft = hover ? (hover.x > size.w - 380 * size.rem ? hover.x - 370 * size.rem : hover.x + 24 * size.rem) : 0;

  return (
    <div ref={containerRef} className={styles.chart} onMouseMove={onMouseMove} onMouseLeave={onMouseLeave}>
      <canvas ref={canvasRef} className={styles.canvas} />
      <div ref={probeRef} className={styles.remProbe} />
      {lines.length === 0 && <div className={styles.empty}>Tick a row on the left to plot it.</div>}
      {hover && hoverLine && hoverValue !== undefined && (
        <div className={styles.tooltip} style={{ left: `${tooltipLeft}px`, top: `${Math.max(0, hover.y - 30)}px` }}>
          <div className={styles.tooltipTitle}>
            <div className={styles.tooltipSwatch} style={{ backgroundColor: hoverLine.color }} />
            {hoverLine.label}
          </div>
          <div className={styles.tooltipValue}>{formatMoney(hoverLine.negateInTooltip ? -hoverValue : hoverValue)} / month</div>
          <div className={styles.tooltipTime}>{formatMonthsAgo(monthsAgo[hover.index] ?? 0)}</div>
        </div>
      )}
    </div>
  );
};
