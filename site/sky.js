// Звёздное небо Nova: мерцающие звёзды, медленный дрейф и сверхновая в правом верхнем углу.
// Та же идея, что у StarfieldView в приложении, только на canvas.
(() => {
  const canvas = document.getElementById("sky");
  const ctx = canvas.getContext("2d");
  const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  let w = 0, h = 0, dpr = 1, stars = [];

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    w = canvas.clientWidth;
    h = canvas.clientHeight;
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    // Плотность — от площади экрана, чтобы на телефоне не было каши.
    const count = Math.round((w * h) / 5200);
    stars = Array.from({ length: count }, () => ({
      x: Math.random() * w,
      y: Math.random() * h,
      r: Math.random() < 0.08 ? 1.3 + Math.random() * 0.8 : 0.4 + Math.random() * 0.7,
      phase: Math.random() * Math.PI * 2,
      speed: 0.4 + Math.random() * 1.4,
      drift: 0.02 + Math.random() * 0.06,
      red: Math.random() < 0.06,
    }));
  }

  function supernova(t) {
    // Пульсация «вспышки» с периодом ~7 с.
    const pulse = 0.85 + 0.15 * Math.sin(t / 1100);
    const cx = w * 0.88, cy = h * 0.06;
    const R = Math.max(w, h) * 0.55 * pulse;
    const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, R);
    g.addColorStop(0, "rgba(255, 120, 130, 0.55)");
    g.addColorStop(0.08, "rgba(229, 48, 63, 0.38)");
    g.addColorStop(0.35, "rgba(229, 48, 63, 0.10)");
    g.addColorStop(1, "rgba(229, 48, 63, 0)");
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, w, h);
  }

  function frame(t) {
    ctx.clearRect(0, 0, w, h);
    supernova(still ? 0 : t);

    for (const s of stars) {
      const a = still ? 0.8 : 0.45 + 0.55 * Math.abs(Math.sin(s.phase + (t / 1000) * s.speed));
      if (!still) {
        s.x -= s.drift;
        if (s.x < -2) s.x = w + 2;
      }
      ctx.globalAlpha = a;
      ctx.fillStyle = s.red ? "#ff7a85" : "#ffffff";
      ctx.beginPath();
      ctx.arc(s.x, s.y, s.r, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.globalAlpha = 1;

    if (!still) requestAnimationFrame(frame);
  }

  resize();
  window.addEventListener("resize", () => { resize(); if (still) frame(0); });
  requestAnimationFrame(frame);
})();
