import { useEffect, useState, useRef } from "react";

interface StarData {
  date: string;
  stars: number;
}

export default function StarChart() {
  const [data, setData] = useState<StarData[]>([]);
  const [total, setTotal] = useState<number | null>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    // Get total stars
    fetch("https://api.github.com/repos/jasperdevs/yoink")
      .then((r) => r.json())
      .then((d) => setTotal(d.stargazers_count))
      .catch(() => {});

    // Get star history from stargazers API (paginated, get timestamps)
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

      // Group by date and build cumulative
      const sorted = allStars.sort((a, b) => a.date.localeCompare(b.date));
      const byDate = new Map<string, number>();
      sorted.forEach((s, i) => {
        const day = s.date.slice(0, 10);
        byDate.set(day, i + 1);
      });

      const points: StarData[] = [];
      let lastCount = 0;
      const entries = Array.from(byDate.entries());

      // Fill gaps between dates
      if (entries.length > 0) {
        const startDate = new Date(entries[0][0]);
        const endDate = new Date(entries[entries.length - 1][0]);
        const dateMap = new Map(entries);

        for (let d = new Date(startDate); d <= endDate; d.setDate(d.getDate() + 1)) {
          const key = d.toISOString().slice(0, 10);
          if (dateMap.has(key)) lastCount = dateMap.get(key)!;
          points.push({ date: key, stars: lastCount });
        }
      }

      setData(points);
    }

    fetchStarHistory();
  }, []);

  useEffect(() => {
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

    // Clear
    ctx.clearRect(0, 0, w, h);

    // Grid lines and Y labels
    ctx.strokeStyle = "rgba(255,255,255,0.06)";
    ctx.lineWidth = 1;
    ctx.fillStyle = "rgba(255,255,255,0.3)";
    ctx.font = "10px 'IBM Plex Mono', monospace";
    ctx.textAlign = "right";
    const yTicks = 5;
    for (let i = 0; i <= yTicks; i++) {
      const val = Math.round((niceMax / yTicks) * i);
      const y = padT + plotH - (i / yTicks) * plotH;
      ctx.beginPath();
      ctx.moveTo(padL, y);
      ctx.lineTo(w - padR, y);
      ctx.stroke();
      ctx.fillText(val.toString(), padL - 8, y + 3);
    }

    // X labels (dates)
    ctx.textAlign = "center";
    const xTicks = Math.min(6, data.length);
    for (let i = 0; i < xTicks; i++) {
      const idx = Math.round((i / (xTicks - 1)) * (data.length - 1));
      const x = padL + (idx / (data.length - 1)) * plotW;
      const date = new Date(data[idx].date);
      const label = `${date.getMonth() + 1}/${date.getDate()}`;
      ctx.fillText(label, x, h - padB + 16);
    }

    // Build path
    const points: [number, number][] = data.map((d, i) => [
      padL + (i / (data.length - 1)) * plotW,
      padT + plotH - (d.stars / niceMax) * plotH,
    ]);

    // Hatching fill (diagonal lines under the curve)
    ctx.save();
    ctx.beginPath();
    ctx.moveTo(points[0][0], padT + plotH);
    points.forEach(([x, y]) => ctx.lineTo(x, y));
    ctx.lineTo(points[points.length - 1][0], padT + plotH);
    ctx.closePath();
    ctx.clip();

    ctx.strokeStyle = "rgba(255,255,255,0.07)";
    ctx.lineWidth = 1;
    const step = 6;
    for (let i = -h; i < w + h; i += step) {
      ctx.beginPath();
      ctx.moveTo(i, 0);
      ctx.lineTo(i + h, h);
      ctx.stroke();
    }
    ctx.restore();

    // Main line
    ctx.beginPath();
    ctx.moveTo(points[0][0], points[0][1]);
    for (let i = 1; i < points.length; i++) {
      ctx.lineTo(points[i][0], points[i][1]);
    }
    ctx.strokeStyle = "rgba(255,255,255,0.5)";
    ctx.lineWidth = 1.5;
    ctx.stroke();

    // Glow line
    ctx.beginPath();
    ctx.moveTo(points[0][0], points[0][1]);
    for (let i = 1; i < points.length; i++) {
      ctx.lineTo(points[i][0], points[i][1]);
    }
    ctx.strokeStyle = "rgba(255,255,255,0.15)";
    ctx.lineWidth = 4;
    ctx.stroke();
  }, [data]);

  const label =
    total !== null
      ? total >= 1000
        ? `${(total / 1000).toFixed(1)}K`
        : total.toString()
      : "...";

  return (
    <div>
      <div className="rounded-lg border border-zinc-800 overflow-hidden bg-zinc-950">
        <canvas
          ref={canvasRef}
          className="w-full"
          style={{ height: 260 }}
        />
      </div>
      <p className="text-xs text-zinc-600 mt-3 text-center">
        Fig 1. <span className="text-zinc-300 font-semibold">{label}</span> GitHub Stars
      </p>
    </div>
  );
}
