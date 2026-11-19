export const state = {
  q: "",
  brand: "",
  category: "",
  sort: "name",
  minPrice: "",
  maxPrice: "",
  attrs: [], // ["Renk:Bej", "Özellik:Organik", ...]
  onSale: false,
  allProducts: [],
  page: 1,
  pageSize: 24,
  totalCount: 0,
  totalPages: 0,
};

export const PAGE_SIZE = 24;

// Mutated in place (categories.length = 0; categories.push(...)) rather than reassigned,
// so every module that imports this binding keeps seeing the live list — see
// categoryMenu.js's loadCategories.
export const categories = [];
