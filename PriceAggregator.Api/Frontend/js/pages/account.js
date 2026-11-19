// Entry point for account.html — "Hesabım": favorites tab + editable profile tab.
// Header/search/category-menu wiring mirrors home.js (no in-page filtering, search just
// navigates to products.html like the homepage does).
import { $, isValidEmail, isValidPhone } from "../shared/utils.js";
import { initCategoryMenu, loadCategories, closeCategoryMenu } from "../modules/categoryMenu.js";
import { initModal, closeModal } from "../modules/modal.js";
import { initLoginModal, openLoginModal, openLogoutModal, closeLoginModal, closeLogoutModal } from "../modules/loginModal.js";
import { initSearchBox } from "../modules/search.js";
import { renderProductCard } from "../modules/productGrid.js";
import { onFavoritesChange } from "../shared/favorites.js";
import { getSession, setSession, onAuthChange, apiFetch } from "../shared/auth.js";

function goToProducts(params) {
  const qs = new URLSearchParams(params).toString();
  window.location.href = qs ? `products.html?${qs}` : "products.html";
}

initCategoryMenu((slug) => goToProducts({ category: slug }));
initModal();
initLoginModal();
initSearchBox({
  onSubmit: (term) => {
    if (term) goToProducts({ q: term });
  },
  onBrandPick: (brand) => goToProducts({ brand }),
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    closeCategoryMenu();
    closeModal();
    closeLoginModal();
    closeLogoutModal();
  }
});

loadCategories();

/* ── Tabs ── */
function setActiveTab(tab) {
  $("tabFavorites").classList.toggle("active", tab === "favorites");
  $("tabProfile").classList.toggle("active", tab === "profile");
  $("favoritesPanel").classList.toggle("hidden", tab !== "favorites");
  $("profilePanel").classList.toggle("hidden", tab !== "profile");
}
$("tabFavorites").addEventListener("click", () => setActiveTab("favorites"));
$("tabProfile").addEventListener("click", () => setActiveTab("profile"));

/* ── Favorites ── */
let favoritesLoaded = false;

async function loadFavorites() {
  try {
    const items = await apiFetch("/favorites");
    const grid = $("favoritesGrid");
    grid.innerHTML = "";

    $("favoritesCountBadge").textContent = items.length;
    $("favoritesCountBadge").classList.toggle("hidden", items.length === 0);

    if (items.length === 0) {
      $("favoritesEmpty").classList.remove("hidden");
      $("favoritesHeader").classList.add("hidden");
      return;
    }

    $("favoritesEmpty").classList.add("hidden");
    $("favoritesHeader").classList.remove("hidden");
    $("favoritesCount").textContent = `${items.length} ürün`;
    items.forEach((p) => grid.appendChild(renderProductCard(p)));
  } catch (err) {
    console.error("Favoriler yüklenemedi:", err);
  }
}

// The heart button on each card (shared with the rest of the site, see productGrid.js)
// unfavorites in place — reload this tab's list whenever that happens while it's visible.
onFavoritesChange(() => {
  if (favoritesLoaded && !$("favoritesPanel").classList.contains("hidden")) loadFavorites();
});

/* ── Profile ── */
const PROFILE_FIELD_IDS = ["profileName", "profileSurname", "profileEmail", "profilePhone", "profileBirthdate", "profileCity"];
let profileDraft = null;

$("profileBirthdate").max = new Date().toISOString().slice(0, 10);

function populateProfileForm(user) {
  profileDraft = user;
  $("profileName").value = user.Name || user.DisplayName || "";
  $("profileSurname").value = user.Surname || "";
  $("profileEmail").value = user.Email || "";
  $("profilePhone").value = user.Phone || "";
  $("profileBirthdate").value = user.BirthDate ? user.BirthDate.slice(0, 10) : "";
  $("profileCity").value = user.City || "";

  const firstName = (user.Name || user.DisplayName || user.Email || "?").trim();
  const lastName = (user.Surname || "").trim();
  $("profileAvatar").textContent = `${firstName.charAt(0)}${lastName.charAt(0) || ""}`.toUpperCase();
  $("profileAvatarName").textContent = [user.Name || user.DisplayName, user.Surname].filter(Boolean).join(" ") || user.Email;
}

function setProfileEditing(editing) {
  PROFILE_FIELD_IDS.forEach((id) => {
    $(id).disabled = !editing;
  });
  $("profileActions").classList.toggle("hidden", !editing);
  $("profileEditBtn").classList.toggle("hidden", editing);
}

$("profileEditBtn").addEventListener("click", () => {
  $("profileSavedMsg").classList.add("hidden");
  setProfileEditing(true);
});

$("profileCancelBtn").addEventListener("click", () => {
  populateProfileForm(profileDraft);
  setProfileEditing(false);
});

$("profilePhone").addEventListener("input", (e) => {
  e.target.value = e.target.value.replace(/\D/g, "").slice(0, 11);
});

$("profileForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("profileErrorMsg").classList.add("hidden");
  $("profileSavedMsg").classList.add("hidden");

  const email = $("profileEmail").value.trim();
  const phone = $("profilePhone").value.trim();
  const birthdateValue = $("profileBirthdate").value;

  if (!isValidEmail(email)) {
    $("profileErrorMsg").textContent = "Geçerli bir e-posta adresi giriniz (örnek: ad@alanadi.com).";
    $("profileErrorMsg").classList.remove("hidden");
    return;
  }
  if (phone && !isValidPhone(phone)) {
    $("profileErrorMsg").textContent = "Geçerli bir telefon numarası giriniz (örnek: 05xxxxxxxxx).";
    $("profileErrorMsg").classList.remove("hidden");
    return;
  }
  if (birthdateValue && birthdateValue > $("profileBirthdate").max) {
    $("profileErrorMsg").textContent = "Doğum tarihi bugünden sonraki bir tarih olamaz.";
    $("profileErrorMsg").classList.remove("hidden");
    return;
  }

  $("profileSaveBtn").disabled = true;

  try {
    const updatedUser = await apiFetch("/auth/me", {
      method: "PUT",
      body: JSON.stringify({
        Email: $("profileEmail").value.trim(),
        Name: $("profileName").value.trim(),
        Surname: $("profileSurname").value.trim(),
        Phone: $("profilePhone").value.trim(),
        BirthDate: $("profileBirthdate").value || null,
        City: $("profileCity").value || null,
      }),
    });

    const session = getSession();
    setSession(session.token, updatedUser, session.remember);
    setProfileEditing(false);
    $("profileSavedMsg").classList.remove("hidden");
  } catch (err) {
    $("profileErrorMsg").textContent = err.message;
    $("profileErrorMsg").classList.remove("hidden");
  } finally {
    $("profileSaveBtn").disabled = false;
  }
});

/* ── Guest / logged-in state ── */
function renderAccountState() {
  const session = getSession();
  $("accountGuest").classList.toggle("hidden", !!session);
  $("accountLoggedIn").classList.toggle("hidden", !session);
  if (!session) return;

  $("accountName").textContent = session.user.DisplayName || session.user.Email.split("@")[0];
  $("accountEmail").textContent = session.user.Email;
  populateProfileForm(session.user);
  setProfileEditing(false);

  if (!favoritesLoaded) {
    favoritesLoaded = true;
    loadFavorites();
  }
}

$("accountGuestLoginBtn").addEventListener("click", () => openLoginModal());
$("accountLogoutBtn").addEventListener("click", () => openLogoutModal());

onAuthChange(renderAccountState);
renderAccountState();
