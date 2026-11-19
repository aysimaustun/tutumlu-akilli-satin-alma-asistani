import { $, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { openProductDetail } from "./modal.js";

// Type-ahead dropdown under the search bar: as the shopper types, shows matching
// categories/brands/products or weekly trending searches when focused with empty input.
let searchSuggestionsDebounce = null;
let cachedTrending = null;

// What happens on a search submit / brand-suggestion pick — set by whichever page called
// initSearchBox (products.js filters in place; home.js navigates to products.html).
let onSubmit = () => {};
let onBrandPick = () => {};

function closeSearchSuggestions() {
  $("searchSuggestions").innerHTML = "";
  $("searchSuggestions").classList.remove("open");
}

function renderTrendingSearches(trends) {
  const container = $("searchSuggestions");
  container.innerHTML = "";

  if (!trends || trends.length === 0) {
    closeSearchSuggestions();
    return;
  }

  const section = document.createElement("div");
  section.className = "search-suggestions-section search-trending-section";
  section.innerHTML = `<p class="search-suggestions-label">🔥 HAFTANIN TREND ARAMALARI</p>`;

  const chipsWrap = document.createElement("div");
  chipsWrap.className = "search-trending-chips";

  trends.forEach((item) => {
    const term = typeof item === "string" ? item : item.Term;
    if (!term) return;

    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "search-trending-chip";
    btn.innerHTML = `<span class="trending-icon">🔥</span><span class="trending-text">${escapeHtml(term)}</span>`;
    btn.addEventListener("click", () => {
      closeSearchSuggestions();
      $("searchInput").value = term;
      onSubmit(term);
    });
    chipsWrap.appendChild(btn);
  });

  section.appendChild(chipsWrap);
  container.appendChild(section);
  container.classList.add("open");
}

async function loadTrendingSearches() {
  const term = $("searchInput").value.trim();
  if (term.length >= 2) return;

  if (cachedTrending && cachedTrending.length > 0) {
    renderTrendingSearches(cachedTrending);
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/popular-searches`);
    if (!res.ok) return;
    const data = await res.json();
    cachedTrending = data;
    if ($("searchInput").value.trim().length < 2) {
      renderTrendingSearches(data);
    }
  } catch (err) {
    console.error("Failed to load trending searches:", err);
  }
}

function renderSearchSuggestions(data) {
  const container = $("searchSuggestions");
  container.innerHTML = "";

  const hasAny = data.Brands.length > 0 || data.Products.length > 0;
  if (!hasAny) {
    closeSearchSuggestions();
    return;
  }

  if (data.Brands.length > 0) {
    const section = document.createElement("div");
    section.className = "search-suggestions-section";
    section.innerHTML = `<p class="search-suggestions-label">Markalar</p>`;
    data.Brands.forEach((brand) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "search-suggestion-item";
      btn.innerHTML = `${escapeHtml(brand)} <span class="search-suggestion-tag">Marka</span>`;
      btn.addEventListener("click", () => {
        closeSearchSuggestions();
        onBrandPick(brand);
      });
      section.appendChild(btn);
    });
    container.appendChild(section);
  }

  if (data.Products.length > 0) {
    const section = document.createElement("div");
    section.className = "search-suggestions-section";
    section.innerHTML = `<p class="search-suggestions-label">Önerilen Ürünler</p>`;
    data.Products.forEach((p) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "search-suggestion-product";
      const imageHtml = p.ImageUrl
        ? `<img src="${escapeHtml(p.ImageUrl)}" alt="" loading="lazy">`
        : `<span class="search-suggestion-product-placeholder"></span>`;
      btn.innerHTML = `${imageHtml}<span>${escapeHtml(p.CanonicalTitle)}</span>`;
      btn.addEventListener("click", () => {
        closeSearchSuggestions();
        openProductDetail(p.Id, p.CanonicalTitle);
      });
      section.appendChild(btn);
    });
    container.appendChild(section);
  }

  container.classList.add("open");
}

async function loadSearchSuggestions(term) {
  try {
    const res = await fetch(`${API_BASE}/search-suggestions?q=${encodeURIComponent(term)}`);
    const data = await res.json();
    // The input may have changed (or been cleared) while this request was in flight.
    if ($("searchInput").value.trim() === term) renderSearchSuggestions(data);
  } catch (err) {
    console.error("Failed to load search suggestions:", err);
  }
}

export function initSearchBox(handlers) {
  onSubmit = handlers.onSubmit;
  onBrandPick = handlers.onBrandPick;

  $("searchForm").addEventListener("submit", (e) => {
    e.preventDefault();
    closeSearchSuggestions();
    onSubmit($("searchInput").value.trim());
  });

  $("searchInput").addEventListener("focus", () => {
    const term = $("searchInput").value.trim();
    if (term.length < 2) {
      loadTrendingSearches();
    }
  });

  $("searchInput").addEventListener("click", () => {
    const term = $("searchInput").value.trim();
    if (term.length < 2) {
      loadTrendingSearches();
    }
  });

  $("searchInput").addEventListener("input", () => {
    const term = $("searchInput").value.trim();
    clearTimeout(searchSuggestionsDebounce);

    if (term.length < 2) {
      loadTrendingSearches();
      return;
    }

    searchSuggestionsDebounce = setTimeout(() => loadSearchSuggestions(term), 250);
  });

  $("searchInput").addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeSearchSuggestions();
  });

  document.addEventListener("click", (e) => {
    const wrap = document.querySelector(".search-bar-wrap");
    if (!wrap.contains(e.target)) closeSearchSuggestions();
  });
}
