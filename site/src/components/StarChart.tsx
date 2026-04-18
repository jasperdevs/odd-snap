import { useEffect, useState, useRef, useCallback } from "react";

interface StarData {
  date: string;
  stars: number;
}

interface CachedStarData {
  timestamp: number;
  data: StarData[];
  total: number | null;
}

interface ChartLayout {
  padL: number;
  padR: number;
  padT: number;
  padB: number;
  plotW: number;
  plotH: number;
  w: number;
  h: number;
  niceMax: number;
}

const CACHE_KEY = "yoink-star-chart";
const CACHE_TTL = 24 * 60 * 60 * 1000; // 24 hours

function getCachedData(): CachedStarData | null {
  try {
    const raw = localStorage.getItem(CACHE_KEY);
    if (!raw) return null;
    const cached: CachedStarData = JSON.parse(raw);
    if (Date.now() - cached.timestamp < CACHE_TTL && cached.data.length > 0) {
      return cached;
    }
  } catch {}
  return null;
}

function setCachedData(data: StarData[], total: number | null) {
  try {
    const cached: CachedStarData = { timestamp: Date.now(), data, total };
    localStorage.setItem(CACHE_KEY, JSON.stringify(cached));
  } catch {}
}

export default function StarChart() {
  const [data, setData] = useState<StarData[]>([]);
  const [total, setTotal] = useState<number | null>(null);
  const [hover, setHover] = useState<{ x: number; y: number; date: string; stars: number } | null>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const layoutRef = useRef<ChartLayout | null>(null);

  useEffect(() => {
    const cached = getCachedData();
    if (cached) {
      setData(cached.data);
      setTotal(cached.total);
      return;
    }

    fetch("https://api.github.com/repos/jasperdevs/yoink")
      .then((r) => r.json())
      .then((d) => setTotal(d.stargazers_count))
      .catch(() => {});

    async function fetchStarHistory() {
      const perPage = 100;
      let page = 1;
      let allStars: { date: string }[] = [];

      while (true) {
        try {
          const res = await fetch(
            `https://api.github.com/repos/jasperdevs/yoink/stargazers?per_page=${perPage}&page=${page}`,
            { headers: { Accept: "application/vnd.github.v3.star+json" } }
          );
          if (!res.ok) break;
          const batch = await res.json();
          if (!batch.length) break;
          allStars = allStars.concat(
            batch.map((s: { starred_at: string }) => ({ date: s.starred_at }))
          );
          if (batch.length < perPage) break;
          page++;
        } catch {
          break;
        }
      }

      if (allStars.length === 0) return;

      const sorted = allStars.sort((a, b) => a.date.localeCompare(b.date));
      const byDate = new Map<string, number>();
      sorted.forEach((s, i) => {
        const day = s.date.slice(0, 10);
        byDate.set(day, i + 1);
      });

      const points: StarData[] = [];
      let lastCount = 0;
      const entries = Array.from(byDate.entries());

      if (entries.length > 0) {
        const firstStarDate = new Date(entries[0][0] + "T00:00:00Z");
        const startDate = new Date(firstStarDate);
        startDate.setUTCDate(startDate.getUTCDate() - 1);
        const today = new Date();
        const todayStr = today.getUTCFullYear() + "-" + String(today.getUTCMonth() + 1).padStart(2, "0") + "-" + String(today.getUTCDate()).padStart(2, "0");
        const endDate = new Date(todayStr + "T00:00:00Z");
        const dateMap = new Map(entries);

        points.push({ date: startDate.toISOString().slice(0, 10), stars: 0 });

        for (let d = new Date(firstStarDate); d <= endDate; d.setUTCDate(d.getUTCDate() + 1)) {
          const key = d.toISOString().slice(0, 10);
          if (dateMap.has(key)) lastCount = dateMap.get(key)!;
          points.push({ date: key, stars: lastCount });
        }
      }

      setData(points);
      setTotal((prev) => {
        setCachedData(points, prev);
        return prev;
      });
    }

    fetchStarHistory();
  }, []);

  const drawChart = useCallback((hoverIdx?: number) => {
    const canvas = canvasRef.current;
    if (!canvas || data.length < 2) return;

    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    const ctx = canvas.getContext("2d")!;
    ctx.scale(dpr, dpr);

    const w = rect.width;
    const h = rect.height;
    const padL = 50;
    const padR = 16;
    const padT = 16;
    const padB = 36;
    const plotW = w - padL - padR;
    const plotH = h - padT - padB;

    const maxStars = Math.max(...data.map((d) => d.stars));
    const niceMax = Math.ceil(maxStars / 10) * 10 || 10;

    layoutRef.current = { padL, padR, padT, padB, plotW, plotH, w, h, niceMax };

    ctx.clearRect(0, 0, w, h);

    // Grid lines and Y labels
    ctx.strokeStyle = "rgba(0,0,0,0.06)";
    ctx.lineWidth = 1;
    ctx.fillStyle = "rgba(0,0,0,0.5)";
    ctx.font = "11px 'Inter Variable', 'Inter', ui-sans-serif, system-ui, sans-serif";
    ctx.textAlign = "right";
    const yTicks = 4;
    for (let i = 0; i <= yTicks; i++) {
      const val = Math.round((niceMax / yTicks) * i);
      const y = padT + plotH - (i / yTicks) * plotH;
      ctx.beginPath();
      ctx.moveTo(padL, y);
      ctx.lineTo(w - padR, y);
      ctx.stroke();
      ctx.fillText(val.toString(), padL - 8, y + 3);
    }

    // X labels - use UTC to avoid timezone offset issues
    ctx.textAlign = "center";
    const xTicks = Math.min(6, data.length);
    for (let i = 0; i < xTicks; i++) {
      const idx = Math.round((i / (xTicks - 1)) * (data.length - 1));
      const x = padL + (idx / (data.length - 1)) * plotW;
      const parts = data[idx].date.split("-");
      const moIdx = parseInt(parts[1], 10) - 1;
      const day = parseInt(parts[2], 10);
      const mo = ['jan','feb','mar','apr','may','jun','jul','aug','sep','oct','nov','dec'][moIdx];
      const label = `${mo} ${day}`;
      ctx.fillText(label, x, h - padB + 16);
    }

    // Build path points
    const points: [number, number][] = data.map((d, i) => [
      padL + (i / (data.length - 1)) * plotW,
      padT + plotH - (d.stars / niceMax) * plotH,
    ]);

    // Area fill (subtle vertical gradient)
    const gradient = ctx.createLinearGradient(0, padT, 0, padT + plotH);
    gradient.addColorStop(0, "rgba(0,0,0,0.08)");
    gradient.addColorStop(1, "rgba(0,0,0,0)");
    ctx.beginPath();
    ctx.moveTo(points[0][0], padT + plotH);
    points.forEach(([x, y]) => ctx.lineTo(x, y));
    ctx.lineTo(points[points.length - 1][0], padT + plotH);
    ctx.closePath();
    ctx.fillStyle = gradient;
    ctx.fill();

    // Main line
    ctx.beginPath();
    ctx.moveTo(points[0][0], points[0][1]);
    for (let i = 1; i < points.length; i++) ctx.lineTo(points[i][0], points[i][1]);
    ctx.strokeStyle = "rgba(0,0,0,0.85)";
    ctx.lineWidth = 1.25;
    ctx.stroke();

    // Hover crosshair + dot
    if (hoverIdx !== undefined && hoverIdx >= 0 && hoverIdx < points.length) {
      const [hx, hy] = points[hoverIdx];

      ctx.strokeStyle = "rgba(0,0,0,0.25)";
      ctx.lineWidth = 1;
      ctx.setLineDash([3, 3]);
      ctx.beginPath();
      ctx.moveTo(hx, padT);
      ctx.lineTo(hx, padT + plotH);
      ctx.stroke();
      ctx.setLineDash([]);

      ctx.beginPath();
      ctx.arc(hx, hy, 4, 0, Math.PI * 2);
      ctx.fillStyle = "#000";
      ctx.fill();
      ctx.beginPath();
      ctx.arc(hx, hy, 2, 0, Math.PI * 2);
      ctx.fillStyle = "#fff";
      ctx.fill();
    }
  }, [data]);

  useEffect(() => { drawChart(); }, [drawChart]);

  const handleMouseMove = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const layout = layoutRef.current;
    if (!canvas || !layout || data.length < 2) { setHover(null); return; }

    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;

    const idx = Math.round(((mx - layout.padL) / layout.plotW) * (data.length - 1));
    if (idx < 0 || idx >= data.length) { setHover(null); drawChart(); return; }

    const pt = data[idx];
    const px = layout.padL + (idx / (data.length - 1)) * layout.plotW;
    const py = layout.padT + layout.plotH - (pt.stars / layout.niceMax) * layout.plotH;

    setHover({ x: px, y: py, date: pt.date, stars: pt.stars });
    drawChart(idx);
  }, [data, drawChart]);

  const handleMouseLeave = useCallback(() => {
    setHover(null);
    drawChart();
  }, [drawChart]);

  const label =
    total !== null
      ? total >= 1000
        ? `${(total / 1000).toFixed(1)}k`
        : total.toString()
      : "...";

  // Format tooltip date from the ISO date string directly to avoid timezone issues
  const tooltipDate = hover
    ? (() => {
        const parts = hover.date.split("-");
        const moIdx = parseInt(parts[1], 10) - 1;
        const day = parseInt(parts[2], 10);
        const year = parts[0];
        const mo = ['jan','feb','mar','apr','may','jun','jul','aug','sep','oct','nov','dec'][moIdx];
        return `${mo} ${day}, ${year}`;
      })()
    : "";

  return (
    <div>
      <div className="relative">
        <canvas
          ref={canvasRef}
          className="w-full cursor-crosshair"
          style={{ height: 240 }}
          onMouseMove={handleMouseMove}
          onMouseLeave={handleMouseLeave}
        />
        {hover && (
          <div
            className="absolute pointer-events-none bg-[#F6F6F6] border border-[#EBEBEB] rounded-md px-2.5 py-1.5 text-[12px]"
            style={{
              left: Math.min(hover.x, (layoutRef.current?.w ?? 600) - 140),
              top: Math.max(hover.y - 40, 4),
            }}
          >
            <span className="text-black/60">{tooltipDate}</span>
            <span className="text-black ml-2">{hover.stars} stars</span>
          </div>
        )}
      </div>
      <p className="text-[13px] text-black/60 mt-3">
        <span className="text-black">{label}</span> github stars
      </p>
    </div>
  );
}
