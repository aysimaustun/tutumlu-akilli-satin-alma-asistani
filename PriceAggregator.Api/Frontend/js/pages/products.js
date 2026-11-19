// Entry point for products.html — filter panel + product grid. Reads ?category=/?q=/
// ?brand=/?onSale= from the URL on load (set by index.html's navigation links) so landing
// here from the homepage arrives pre-filtered.
import { $ } from "../shared/utils.js";
import { state } from "../shared/state.js";
import {
  initFilters,
  loadSearchCategories,
  renderSearchCategoryFilter,
  readFilterInputs,
  syncFilterInputs,
  refreshFilterPanels,
} from "../modules/filters.js";
import { initCategoryMenu, loadCategories, closeCategoryMenu } from "../modules/categoryMenu.js";
import { initModal, closeModal } from "../modules/modal.js";
import { initLoginModal, closeLoginModal, closeLogoutModal } from "../modules/loginModal.js";
import { initSearchBox } from "../modules/search.js";
import { loadProducts } from "../modules/productGrid.js";

function applyUrlParams() {
  const params = new URLSearchParams(window.location.search);
  state.category = params.get("category") || "";
  state.q = params.get("q") || "";
  state.brand = params.get("brand") || "";
  state.onSale = params.get("onSale") === "true";
  syncFilterInputs();
}

initFilters();
initCategoryMenu((slug) => {
  state.brand = "";
  state.attrs = [];
  state.minPrice = null;
  state.maxPrice = null;
  state.onSale = false;
  syncFilterInputs();
  renderSearchCategoryFilter();
  refreshFilterPanels();
  loadProducts();
});
initModal();
initLoginModal();
initSearchBox({
  onSubmit: () => {
    readFilterInputs();
    loadSearchCategories(state.q);
    refreshFilterPanels();
    loadProducts();
  },
  onBrandPick: (brand) => {
    $("searchInput").value = "";
    state.q = "";
    state.brand = brand;
    loadSearchCategories("");
    syncFilterInputs();
    refreshFilterPanels();
    loadProducts();
  },
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    closeCategoryMenu();
    closeModal();
    closeLoginModal();
    closeLogoutModal();
  }
});

applyUrlParams();
refreshFilterPanels();
loadCategories();
if (state.q) loadSearchCategories(state.q);
loadProducts();
