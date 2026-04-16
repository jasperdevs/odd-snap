import { useEffect, useState, useRef, useCallback } from "react";
import { fetchGitHubRepoJson, logGitHubFetchError } from "../lib/github";
import type { GitHubRepository, GitHubStarEvent } from "../types/github";

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
const CACHE_TTL = 24 * 60 * 60 * 1000;

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

    fetchGitHubRepoJson<GitHubRepository>()
      .then((payload) => setTotal(payload.stargazers_count))
      .catch((error) => logGitHubFetchError("star count", error));

    async function fetchStarHistory() {
      const perPage = 100;
      let page = 1;
      let allStars: { date: string }[] = [];

      while (true) {
        try {
          const batch = await fetchGitHubRepoJson<GitHubStarEvent[]>(
            `/stargazers?per_page=${perPage}&page=${page}`,
            { headers: { Accept: "application/vnd.github.v3.star+json" } },
          );
          if (!batch.length) break;

          allStars = allStars.concat(batch.map((item) => ({ date: item.starred_at })));
          if (batch.length < perPage) break;
          page += 1;
        } catch (error) {
          logGitHubFetchError("star history", error);
          break;
        }
      }

      if (allStars.length === 0) return;

      const sorted = allStars.sort((a, b) => a.date.localeCompare(b.date));
      const byDate = new Map<string, number>();
      sorted.forEach((star, index) => {
        byDate.set(star.date.slice(0, 10), index + 1);
      });

      const points: StarData[] = [];
      let lastCount = 0;
      const entries = Array.from(byDate.entries());

      if (entries.length > 0) {
        const firstStarDate = new Date(entries[0][0] + "T00:00:00Z");
        const startDate = new Date(firstStarDate);
        startDate.setUTCDate(startDate.getUTCDate() - 1);

        const today = new Date();
        const todayStr = `${today.getUTCFullYear()}-${String(today.getUTCMonth() + 1).padStart(2, "0")}-${String(today.getUTCDate()).padStart(2, "0")}`;
        const endDate = new Date(todayStr + "T00:00:00Z");
        const dateMap = new Map(entries);

        points.push({ date: startDate.toISOString().slice(0, 10), stars: 0 });

        for (let date = new Date(firstStarDate); date <= endDate; date.setUTCDate(date.getUTCDate() + 1)) {
          const key = date.toISOString().slice(0, 10);
          if (dateMap.has(key)) {
            lastCount = dateMap.get(key)!;
          }
          points.push({ date: key, stars: lastCount });
        }
      }

      setData(points);
      setTotal((previous) => {
        setCachedData(points, previous);
        return previous;
      });
    }

    fetchStarHistory();
  }, []);

  const drawChart = useCallback((hoverIndex?: number) => {
    const canvas = canvasRef.current;
    if (!canvas || data.length < 2) return;

    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.scale(dpr, dpr);

    const w = rect.width;
    const h = rect.height;
    const padL = 50;
    const padR = 16;
    const padT = 16;
    const padB = 36;
    const plotW = w - padL - padR;
    const plotH = h - padT - padB;

    const maxStars = Math.max(...data.map((item) => item.stars));
    const niceMax = Math.ceil(maxStars / 10) * 10 || 10;
    layoutRef.current = { padL, padR, padT, padB, plotW, plotH, w, h, niceMax };

    ctx.clearRect(0, 0, w, h);
    ctx.strokeStyle = "rgba(255,255,255,0.06)";
    ctx.lineWidth = 1;
    ctx.fillStyle = "rgba(255,255,255,0.3)";
    ctx.font = "10px 'IBM Plex Mono', monospace";
    ctx.textAlign = "right";

    const yTicks = 5;
    for (let index = 0; index <= yTicks; index += 1) {
      const value = Math.round((niceMax / yTicks) * index);
      const y = padT + plotH - (index / yTicks) * plotH;
      ctx.beginPath();
      ctx.moveTo(padL, y);
      ctx.lineTo(w - padR, y);
      ctx.stroke();
      ctx.fillText(value.toString(), padL - 8, y + 3);
    }

    ctx.textAlign = "center";
    const xTicks = Math.min(6, data.length);
    for (let index = 0; index < xTicks; index += 1) {
      const pointIndex = Math.round((index / (xTicks - 1)) * (data.length - 1));
      const x = padL + (pointIndex / (data.length - 1)) * plotW;
      const parts = data[pointIndex].date.split("-");
      const monthIndex = parseInt(parts[1], 10) - 1;
      const day = parseInt(parts[2], 10);
      const month = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][monthIndex];
      ctx.fillText(`${month} ${day}`, x, h - padB + 16);
    }

    const points: [number, number][] = data.map((item, index) => [
      padL + (index / (data.length - 1)) * plotW,
      padT + plotH - (item.stars / niceMax) * plotH,
    ]);

    ctx.save();
    ctx.beginPath();
    ctx.moveTo(points[0][0], padT + plotH);
    points.forEach(([x, y]) => ctx.lineTo(x, y));
    ctx.lineTo(points[points.length - 1][0], padT + plotH);
    ctx.closePath();
    ctx.clip();

    ctx.strokeStyle = "rgba(255,255,255,0.07)";
    ctx.lineWidth = 1;
    for (let index = -h; index < w + h; index += 6) {
      ctx.beginPath();
      ctx.moveTo(index, 0);
      ctx.lineTo(index + h, h);
      ctx.stroke();
    }
    ctx.restore();

    ctx.beginPath();
    ctx.moveTo(points[0][0], points[0][1]);
    for (let index = 1; index < points.length; index += 1) {
      ctx.lineTo(points[index][0], points[index][1]);
    }
    ctx.strokeStyle = "rgba(255,255,255,0.15)";
    ctx.lineWidth = 4;
    ctx.stroke();

    ctx.beginPath();
    ctx.moveTo(points[0][0], points[0][1]);
    for (let index = 1; index < points.length; index += 1) {
      ctx.lineTo(points[index][0], points[index][1]);
    }
    ctx.strokeStyle = "rgba(255,255,255,0.5)";
    ctx.lineWidth = 1.5;
    ctx.stroke();

    if (hoverIndex !== undefined && hoverIndex >= 0 && hoverIndex < points.length) {
      const [hx, hy] = points[hoverIndex];

      ctx.strokeStyle = "rgba(255,255,255,0.15)";
      ctx.lineWidth = 1;
      ctx.setLineDash([3, 3]);
      ctx.beginPath();
      ctx.moveTo(hx, padT);
      ctx.lineTo(hx, padT + plotH);
      ctx.stroke();
      ctx.setLineDash([]);

      ctx.beginPath();
      ctx.arc(hx, hy, 4, 0, Math.PI * 2);
      ctx.fillStyle = "#fff";
      ctx.fill();
      ctx.beginPath();
      ctx.arc(hx, hy, 2, 0, Math.PI * 2);
      ctx.fillStyle = "#0c0c0c";
      ctx.fill();
    }
  }, [data]);

  useEffect(() => {
    drawChart();
  }, [drawChart]);

  const handleMouseMove = useCallback((event: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    const layout = layoutRef.current;
    if (!canvas || !layout || data.length < 2) {
      setHover(null);
      return;
    }

    const rect = canvas.getBoundingClientRect();
    const mx = event.clientX - rect.left;
    const index = Math.round(((mx - layout.padL) / layout.plotW) * (data.length - 1));
    if (index < 0 || index >= data.length) {
      setHover(null);
      drawChart();
      return;
    }

    const point = data[index];
    const x = layout.padL + (index / (data.length - 1)) * layout.plotW;
    const y = layout.padT + layout.plotH - (point.stars / layout.niceMax) * layout.plotH;

    setHover({ x, y, date: point.date, stars: point.stars });
    drawChart(index);
  }, [data, drawChart]);

  const handleMouseLeave = useCallback(() => {
    setHover(null);
    drawChart();
  }, [drawChart]);

  const label = total !== null ? (total >= 1000 ? `${(total / 1000).toFixed(1)}K` : total.toString()) : "...";

  const tooltipDate = hover
    ? (() => {
        const parts = hover.date.split("-");
        const monthIndex = parseInt(parts[1], 10) - 1;
        const day = parseInt(parts[2], 10);
        const year = parts[0];
        const month = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][monthIndex];
        return `${month} ${day}, ${year}`;
      })()
    : "";

  return (
    <div>
      <div className="site-panel-soft relative overflow-hidden rounded-[1.2rem]">
        <canvas
          ref={canvasRef}
          className="w-full cursor-crosshair"
          style={{ height: 260 }}
          onMouseMove={handleMouseMove}
          onMouseLeave={handleMouseLeave}
        />
        {hover ? (
          <div
            className="pointer-events-none absolute rounded-[0.9rem] border border-[var(--line-strong)] bg-[rgba(11,14,18,0.96)] px-3 py-2 text-xs"
            style={{
              left: Math.min(hover.x, (layoutRef.current?.w ?? 600) - 140),
              top: Math.max(hover.y - 44, 4),
            }}
          >
            <span className="text-[var(--soft)]">{tooltipDate}</span>
            <span className="ml-2 font-semibold text-[var(--text)]">{hover.stars} stars</span>
          </div>
        ) : null}
      </div>
      <p className="mt-3 text-center text-xs text-[var(--soft)]">
        <span className="font-semibold text-[var(--text)]">{label}</span> GitHub stars
      </p>
    </div>
  );
}
