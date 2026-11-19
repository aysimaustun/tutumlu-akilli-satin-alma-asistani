import { $ } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { renderProductCard } from "./productGrid.js";

const RAIL_LIMIT = 10;

async function loadRail(sectionId, containerId, query) {
  const section = $(sectionId);
  const container = $(containerId);
  if (!section || !container) return;

  try {
    const params = query ? `${query}&pageSize=${RAIL_LIMIT}` : `pageSize=${RAIL_LIMIT}`;
    const res = await fetch(`${API_BASE}/products?${params}`);
    if (!res.ok) throw new Error(`API returned ${res.status}`);
    const { items } = await res.json();

    if (!items || items.length === 0) {
      section.hidden = true;
      return;
    }

    container.innerHTML = "";
    items.forEach((p) => container.appendChild(renderProductCard(p)));
    section.hidden = false;
  } catch (err) {
    section.hidden = true;
    console.error(`Failed to load rail ${containerId}:`, err);
  }
}

// Click a rail's prev/next arrow to scroll by roughly one screenful of cards, rather than
// relying on drag/trackpad scrolling alone.
function initRailNav() {
  document.querySelectorAll("[data-rail-prev], [data-rail-next]").forEach((btn) => {
    const targetId = btn.dataset.railPrev || btn.dataset.railNext;
    const direction = btn.dataset.railPrev ? -1 : 1;
    btn.addEventListener("click", () => {
      const track = $(targetId);
      track?.scrollBy({ left: direction * track.clientWidth * 0.9, behavior: "smooth" });
    });
  });
}

export function loadFeaturedRails() {
  initRailNav();
  loadRail("onSaleRail", "onSaleProducts", "onSale=true");
  loadRail("popularRail", "popularProducts", "");
}
