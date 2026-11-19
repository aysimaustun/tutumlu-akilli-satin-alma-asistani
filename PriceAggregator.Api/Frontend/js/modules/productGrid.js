import { $, formatPrice, formatUnitPrice, escapeHtml } from "../shared/utils.js";
import { API_BASE } from "../shared/api.js";
import { state, PAGE_SIZE } from "../shared/state.js";
import { getSession } from "../shared/auth.js";
import { isFavorited, toggleFavorite, onFavoritesChange } from "../shared/favorites.js";
import { renderActiveFilters } from "./filters.js";
import { openProductDetail } from "./modal.js";
import { openLoginModal } from "./loginModal.js";

const HEART_ICON = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
  <path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z" />
</svg>`;

function buildFavoriteButton(productId) {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "product-favorite-btn" + (isFavorited(productId) ? " active" : "");
  btn.dataset.favoriteProductId = productId;
  btn.setAttribute("aria-label", "Favorilere ekle");
  btn.innerHTML = HEART_ICON;

  btn.addEventListener("keydown", (e) => e.stopPropagation());
  btn.addEventListener("click", async (e) => {
    e.stopPropagation();
    if (!getSession()) {
      openLoginModal();
      return;
    }
    btn.disabled = true;
    try {
      await toggleFavorite(productId);
    } catch (err) {
      console.error("Favori güncellenemedi:", err);
    } finally {
      btn.disabled = false;
    }
  });

  return btn;
}

// Keeps every rendered heart button (a product can appear in more than one rail/grid at
// once) in sync whenever the favorited set changes, without re-rendering whole cards.
onFavoritesChange(() => {
  document.querySelectorAll("[data-favorite-product-id]").forEach((btn) => {
    const id = Number(btn.dataset.favoriteProductId);
    btn.classList.toggle("active", isFavorited(id));
  });
});

function showSkeletonLoading(container) {
  container.className = "product-grid";
  container.innerHTML = Array(8)
    .fill(
      `<div class="skeleton-card">
        <div class="skeleton-image"></div>
        <div class="skeleton-body">
          <div class="skeleton-line short"></div>
          <div class="skeleton-line long"></div>
          <div class="skeleton-line medium"></div>
        </div>
      </div>`
    )
    .join("");
}

function showEmptyState(container, message) {
  container.className = "product-grid";
  container.innerHTML = `
    <div class="state-message">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
        <circle cx="11" cy="11" r="8"/>
        <path d="m21 21-4.35-4.35"/>
      </svg>
      <h3>Ürün bulunamadı</h3>
      <p>${message}</p>
    </div>`;
}

function showErrorState(container) {
  container.className = "product-grid";
  container.innerHTML = `
    <div class="state-message">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
        <circle cx="12" cy="12" r="10"/>
        <path d="M12 8v4M12 16h.01"/>
      </svg>
      <h3>Bir hata oluştu</h3>
      <p>Ürünler yüklenemedi. API'nin çalıştığından emin olun.</p>
    </div>`;
}

export function renderProductCard(product) {
  const card = document.createElement("article");
  card.className = "product-card";
  card.setAttribute("role", "button");
  card.tabIndex = 0;

  const imageHtml = product.ImageUrl
    ? `<img src="${escapeHtml(product.ImageUrl)}" class="product-image" alt="${escapeHtml(product.CanonicalTitle)}" loading="lazy">`
    : `<svg class="product-image-placeholder" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
         <rect x="3" y="3" width="18" height="18" rx="2"/>
         <circle cx="8.5" cy="8.5" r="1.5"/>
         <path d="m21 15-5-5L5 21"/>
       </svg>`;

  card.innerHTML = `
    <div class="product-image-wrap">
      ${imageHtml}
      ${product.DiscountPercent ? `<span class="product-discount-badge">%${product.DiscountPercent} daha düşük</span>` : ""}
    </div>
    <div class="product-body">
      ${product.Brand ? `<p class="product-brand">${escapeHtml(product.Brand)}</p>` : ""}
      <h3 class="product-title">${escapeHtml(product.CanonicalTitle)}</h3>
      <div class="product-footer">
        <div>
          <span class="product-price-label">En düşük fiyat</span>
          ${product.DiscountPercent ? `<span class="product-old-price">${formatPrice(product.OldPrice)}</span>` : ""}
          <span class="product-price">${formatPrice(product.LowestPrice)}</span>
          ${formatUnitPrice(product.UnitPrice, product.UnitLabel)}
        </div>
        <span class="product-stores">${product.OfferCount} mağaza</span>
      </div>
    </div>`;

  card.querySelector(".product-image-wrap").appendChild(buildFavoriteButton(product.Id));

  const open = () => openProductDetail(product.Id, product.CanonicalTitle);
  card.addEventListener("click", open);
  card.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      open();
    }
  });

  return card;
}

function buildQueryParams(targetPage = state.page) {
  const params = new URLSearchParams();
  if (state.q) params.set("q", state.q);
  if (state.brand) params.set("brand", state.brand);
  if (state.category) params.set("category", state.category);
  if (state.sort) params.set("sort", state.sort);
  if (state.minPrice) params.set("minPrice", state.minPrice);
  if (state.maxPrice) params.set("maxPrice", state.maxPrice);
  if (state.onSale) params.set("onSale", "true");
  state.attrs.forEach((a) => params.append("attr", a));
  params.set("page", targetPage);
  params.set("pageSize", PAGE_SIZE);
  return params.toString();
}

export async function loadProducts(targetPage = 1) {
  state.page = targetPage;
  const container = $("products");
  showSkeletonLoading(container);
  renderActiveFilters();

  try {
    const query = buildQueryParams(targetPage);
    const url = `${API_BASE}/products?${query}`;
    const res = await fetch(url);
    if (!res.ok) {
      throw new Error(`API returned ${res.status}`);
    }
    const data = await res.json();
    state.allProducts = data.items || [];
    state.totalCount = data.totalCount || 0;
    state.totalPages = data.totalPages || 0;
    state.page = data.page || targetPage;
    renderProductsPage();
  } catch (err) {
    showErrorState(container);
    $("resultsCount").textContent = "";
    $("pagination").innerHTML = "";
    console.error(err);
  }
}

// Renders the current page slice returned by the API plus the pagination controls
function renderProductsPage() {
  const container = $("products");
  const products = state.allProducts;

  container.className = "product-grid";
  container.innerHTML = "";

  $("resultsCount").textContent =
    state.totalCount === 0
      ? "Sonuç bulunamadı"
      : `${state.totalCount} ürün bulundu`;

  if (products.length === 0) {
    $("pagination").innerHTML = "";
    showEmptyState(
      container,
      state.q || state.brand || state.category || state.minPrice || state.maxPrice
        ? "Filtreleri değiştirmeyi veya aramayı temizlemeyi deneyin."
        : "Henüz ürün eklenmemiş."
    );
    return;
  }

  products.forEach((p) => container.appendChild(renderProductCard(p)));

  renderPagination(state.totalPages);
}

// Builds [1, "...", 4, 5, 6, "...", 20]-style page lists so large result sets don't render
// a button per page.
function getPageNumbers(current, total) {
  const pages = [];
  for (let i = 1; i <= total; i++) {
    if (i === 1 || i === total || Math.abs(i - current) <= 1) pages.push(i);
  }

  const withEllipsis = [];
  let previous = null;
  pages.forEach((i) => {
    if (previous !== null && i - previous > 1) withEllipsis.push("...");
    withEllipsis.push(i);
    previous = i;
  });
  return withEllipsis;
}

function renderPagination(totalPages) {
  const container = $("pagination");
  container.innerHTML = "";

  if (totalPages <= 1) return;

  const goToPage = (page) => {
    if (page < 1 || page > totalPages || page === state.page) return;
    loadProducts(page);
    $("products").scrollIntoView({ behavior: "smooth", block: "start" });
  };

  const prevBtn = document.createElement("button");
  prevBtn.type = "button";
  prevBtn.className = "pagination-btn pagination-nav";
  prevBtn.textContent = "‹ Önceki";
  prevBtn.disabled = state.page === 1;
  prevBtn.addEventListener("click", () => goToPage(state.page - 1));
  container.appendChild(prevBtn);

  getPageNumbers(state.page, totalPages).forEach((p) => {
    if (p === "...") {
      const span = document.createElement("span");
      span.className = "pagination-ellipsis";
      span.textContent = "…";
      container.appendChild(span);
      return;
    }

    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "pagination-btn" + (p === state.page ? " active" : "");
    btn.textContent = p;
    btn.addEventListener("click", () => goToPage(p));
    container.appendChild(btn);
  });

  const nextBtn = document.createElement("button");
  nextBtn.type = "button";
  nextBtn.className = "pagination-btn pagination-nav";
  nextBtn.textContent = "Sonraki ›";
  nextBtn.disabled = state.page === totalPages;
  nextBtn.addEventListener("click", () => goToPage(state.page + 1));
  container.appendChild(nextBtn);
}
