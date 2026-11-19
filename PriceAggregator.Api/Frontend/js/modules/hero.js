import { $ } from "../shared/utils.js";
import { categories } from "../shared/state.js";

// Static promotional copy — there's no campaign/banner endpoint on the API, so unlike the
// category rail and product rails below (both backed by real data), this stays hardcoded.
// sectorKeyword ties a slide to a real Level 0 category by name (see goToProducts) so its
// CTA lands on that sector instead of the unfiltered product list.
const slides = [
  {
    eyebrow: "TUTUMLU",
    title: "Türkiye'nin en iyi fiyatlarını karşılaştır",
    desc: "Onlarca mağazanın fiyatlarını tek ekranda karşılaştır, aynı ürünü en ucuza nereden alacağını hemen gör.",
    sectorKeyword: null,
    image: "assets/hero-default.png",
  },
  {
    eyebrow: "MARKET",
    title: "Günlük ihtiyaçlarında en ucuzu bul",
    desc: "Gıda, temizlik ve kişisel bakım ürünlerinde mağazalar arası fiyat farkını kaçırma.",
    sectorKeyword: "market",
    image: "assets/hero-market.png",
  },
  {
    eyebrow: "MOBİLYA",
    title: "Evini bütçene uygun döşe",
    desc: "Koltuktan yatak odasına, aradığın mobilyanın en uygun fiyatını burada bul.",
    sectorKeyword: "mobilya",
    image: "assets/hero-mobilya.png",
  },

  {
    eyebrow: "KOZMETİK",
    title: "Bakım ürünlerinde doğru fiyatı bul",
    desc: "Parfümden kişisel bakıma kadar aradığın ürünleri mağazalar arasında karşılaştır.",
    sectorKeyword: "kozmetik",
    image: "assets/hero-kozmetik.png",
  },
];

let index = 0;

function render() {
  const slide = slides[index];
  const heroSlide = $("heroSlide");

  heroSlide.classList.add("is-changing");

  setTimeout(() => {
    $("heroEyebrow").textContent = slide.eyebrow;
    $("heroTitle").textContent = slide.title;
    $("heroDesc").textContent = slide.desc;
    $("heroImage").src = slide.image;

    const dots = $("heroDots");
    dots.innerHTML = "";

    slides.forEach((_, i) => {
      const dot = document.createElement("button");
      dot.type = "button";
      dot.className = "hero-dot" + (i === index ? " active" : "");
      dot.setAttribute("aria-label", `${i + 1}. slayda git`);

      dot.addEventListener("click", () => {
        index = i;
        render();
      });

      dots.appendChild(dot);
    });

    heroSlide.classList.remove("is-changing");

  }, 400);
}

function move(direction) {
  index = (index + direction + slides.length) % slides.length;
  render();
}

// Matches the slide's sectorKeyword against the real Level 0 categories (loaded separately
// by categoryMenu.js's loadCategories into this same shared `categories` array) so the CTA
// lands on that sector's actual slug — falls back to the unfiltered list when categories
// haven't loaded yet or no sector name matches.
function goToProducts(slide) {
  if (slide.sectorKeyword) {
    const sector = categories.find(
      (c) => c.ParentCategoryId == null && c.Name.toLocaleLowerCase("tr-TR").includes(slide.sectorKeyword)
    );
    if (sector) {
      window.location.href = `products.html?category=${encodeURIComponent(sector.Slug)}`;
      return;
    }
  }
  window.location.href = "products.html";
}

export function initHero() {
  $("heroPrev").addEventListener("click", () => move(-1));
  $("heroNext").addEventListener("click", () => move(1));
  $("heroCta").addEventListener("click", () => goToProducts(slides[index]));
  render();
}
