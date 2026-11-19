import { $, escapeHtml, formatPrice, formatUnitPrice } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";

let priceChartInstance = null;

export async function openProductDetail(masterId, title) {
  const modal = $("modal");
  const offersContainer = $("offers");
  const similarContainer = $("similarProducts");
  $("modalTitle").textContent = title;
  offersContainer.innerHTML = `<div class="state-message"><p>Yükleniyor...</p></div>`;
  similarContainer.innerHTML = `<div class="state-message"><p>Yükleniyor...</p></div>`;
  modal.classList.remove("hidden");
  document.body.style.overflow = "hidden";

  let offers = [];
  try {
    const res = await fetch(`${API_BASE}/products/${masterId}`);
    offers = await res.json();
    renderOffers(offersContainer, offers);
  } catch (err) {
    offersContainer.innerHTML = `<div class="state-message"><p>Fiyatlar yüklenemedi.</p></div>`;
    console.error(err);
  }

  // Fetch price history with offers guaranteed to be loaded
  fetch(`${API_BASE}/products/${masterId}/price-history`)
    .then((res) => res.json())
    .then((history) => renderPriceHistoryChart(history, offers))
    .catch((err) => {
      console.error("Price history fetch error:", err);
      renderPriceHistoryChart([], offers);
    });

  // Fetch similar products in parallel
  fetch(`${API_BASE}/products/${masterId}/similar`)
    .then((res) => res.json())
    .then((products) => renderSimilarProducts(similarContainer, products))
    .catch((err) => {
      similarContainer.innerHTML = `<div class="state-message"><p>Benzer ürünler yüklenemedi.</p></div>`;
      console.error(err);
    });
}

function renderOffers(offersContainer, offers) {
  offersContainer.innerHTML = "";
  if (!offers || offers.length === 0) {
    offersContainer.innerHTML = `<div class="state-message"><p>Mağaza teklifi bulunamadı.</p></div>`;
    return;
  }
  offers.forEach((offer, index) => {
    const row = document.createElement("div");
    row.className = "offer-row";
    row.innerHTML = `
      ${offer.ImageUrl ? `<img src="${escapeHtml(offer.ImageUrl)}" alt="${escapeHtml(offer.Source)}">` : ""}
      <div class="offer-store">
        <div class="offer-source">${escapeHtml(offer.Source)}</div>
        ${index === 0 ? '<span class="offer-badge">En ucuz</span>' : ""}
      </div>
      <div class="offer-price-wrap">
        <span class="offer-price">${formatPrice(offer.Price)}</span>
        ${formatUnitPrice(offer.UnitPrice, offer.UnitLabel)}
      </div>
      <a href="${escapeHtml(offer.ProductUrl)}" target="_blank" rel="noopener noreferrer" class="offer-link">
        Git
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>
          <polyline points="15 3 21 3 21 9"/>
          <line x1="10" y1="14" x2="21" y2="3"/>
        </svg>
      </a>`;
    offersContainer.appendChild(row);
  });
}

function renderPriceHistoryChart(historyData, currentOffers) {
  const canvas = $("priceHistoryChart");
  if (!canvas || typeof Chart === "undefined") return;

  if (priceChartInstance) {
    priceChartInstance.destroy();
    priceChartInstance = null;
  }

  let points = [];
  if (Array.isArray(historyData) && historyData.length > 0) {
    points = historyData.map((h) => ({
      Date: h.Date || h.date,
      MinPrice: h.MinPrice ?? h.minPrice ?? h.Price ?? h.price
    }));
  }

  // If no history records exist or only 1 point, build a clean 2-point flat line (Yesterday & Today)
  if (points.length < 2 && currentOffers && currentOffers.length > 0) {
    const lowest = Math.min(...currentOffers.map((o) => o.Price));
    const todayStr = new Date().toISOString().split("T")[0];
    const yestDate = new Date(Date.now() - 86400000);
    const yestStr = yestDate.toISOString().split("T")[0];

    if (points.length === 1) {
      // If we have 1 point, use it as point 1 and today as point 2
      points = [
        { Date: points[0].Date, MinPrice: points[0].MinPrice },
        { Date: todayStr, MinPrice: lowest }
      ];
    } else {
      points = [
        { Date: yestStr, MinPrice: lowest },
        { Date: todayStr, MinPrice: lowest }
      ];
    }
  }

  if (points.length === 0) return;

  const labels = points.map((p) => {
    const d = new Date(p.Date);
    return isNaN(d.getTime()) ? p.Date : d.toLocaleDateString("tr-TR", { day: "numeric", month: "short" });
  });

  const prices = points.map((p) => p.MinPrice);

  const ctx = canvas.getContext("2d");
  const gradient = ctx.createLinearGradient(0, 0, 0, 160);
  gradient.addColorStop(0, "rgba(16, 185, 129, 0.3)");
  gradient.addColorStop(1, "rgba(16, 185, 129, 0.0)");

  priceChartInstance = new Chart(canvas, {
    type: "line",
    data: {
      labels: labels,
      datasets: [
        {
          label: "En Düşük Fiyat (₺)",
          data: prices,
          borderColor: "#10b981",
          borderWidth: 2.5,
          pointBackgroundColor: "#10b981",
          pointBorderColor: "#ffffff",
          pointBorderWidth: 2,
          pointRadius: 4,
          pointHoverRadius: 6,
          fill: true,
          backgroundColor: gradient,
          tension: 0.3
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          backgroundColor: "#0f172a",
          titleColor: "#94a3b8",
          bodyColor: "#10b981",
          bodyFont: { weight: "bold", size: 13 },
          padding: 8,
          cornerRadius: 6,
          displayColors: false,
          callbacks: {
            label: (ctx) => ` ${formatPrice(ctx.raw)}`
          }
        }
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: "#64748b", font: { size: 10 } }
        },
        y: {
          grid: { color: "rgba(226, 232, 240, 0.7)" },
          ticks: {
            color: "#64748b",
            font: { size: 10 },
            callback: (val) => formatPrice(val)
          }
        }
      }
    }
  });
}

function renderSimilarProductCard(p) {
  const card = document.createElement("div");
  card.className = "similar-product-card";
  card.setAttribute("role", "button");
  card.tabIndex = 0;

  const imageHtml = p.ImageUrl
    ? `<img src="${escapeHtml(p.ImageUrl)}" class="similar-product-image" alt="${escapeHtml(p.CanonicalTitle)}" loading="lazy">`
    : `<svg class="similar-product-image-placeholder" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
         <rect x="3" y="3" width="18" height="18" rx="2"/>
         <circle cx="8.5" cy="8.5" r="1.5"/>
         <path d="m21 15-5-5L5 21"/>
       </svg>`;

  card.innerHTML = `
    ${imageHtml}
    <div class="similar-product-info">
      <p class="similar-product-title">${escapeHtml(p.CanonicalTitle)}</p>
      <span class="similar-product-price">${formatPrice(p.LowestPrice)}</span>
      ${formatUnitPrice(p.UnitPrice, p.UnitLabel)}
    </div>`;

  const open = () => openProductDetail(p.Id, p.CanonicalTitle);
  card.addEventListener("click", open);
  card.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      open();
    }
  });

  return card;
}

function renderSimilarProducts(container, products) {
  if (!products || products.length === 0) {
    container.innerHTML = `<div class="state-message"><p>Benzer ürün bulunamadı.</p></div>`;
    return;
  }

  container.innerHTML = "";

  const sameCategory = products.filter((p) => p.SameCategory);
  const related = products.filter((p) => !p.SameCategory);

  if (sameCategory.length > 0) {
    const heading = document.createElement("p");
    heading.className = "similar-group-heading";
    heading.textContent = "Aynı ürün grubu";
    container.appendChild(heading);
    sameCategory.forEach((p) => container.appendChild(renderSimilarProductCard(p)));
  }

  if (related.length > 0) {
    const heading = document.createElement("p");
    heading.className = "similar-group-heading";
    heading.textContent = "İlgili diğer ürünler";
    container.appendChild(heading);
    related.forEach((p) => container.appendChild(renderSimilarProductCard(p)));
  }
}

export function closeModal() {
  $("modal").classList.add("hidden");
  document.body.style.overflow = "";
}

export function initModal() {
  $("closeModal").addEventListener("click", closeModal);
  $("modal").querySelector(".modal-backdrop").addEventListener("click", closeModal);
}
