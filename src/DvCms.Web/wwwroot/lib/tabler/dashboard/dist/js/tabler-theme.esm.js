/*!
* Tabler v1.6.1 (https://tabler.io)
* Copyright 2018-2026 The Tabler Authors
* Copyright 2018-2026 codecalm.net Paweł Kuna
* Licensed under MIT (https://github.com/tabler/tabler/blob/master/LICENSE)
*/
//#region js/src/theme-config.ts
/**
* Every setting the theme switcher understands, with its default value. Each
* key becomes a `data-bs-<key>` attribute on `<html>`, a `tabler-<key>`
* localStorage entry and a `?<key>=` URL parameter, written only when the value
* differs from the default. Adding a key here is all the JavaScript a new
* setting needs; the switcher loops over this object.
*/
var themeDefaults = {
	"theme": "auto",
	"theme-base": "neutral",
	"theme-font": "sans-serif",
	"theme-primary": "blue",
	"theme-radius": "1",
	"layout": "default",
	"navbar": "default",
	"navbar-position": "horizontal",
	"navbar-style": "default",
	"navbar-theme": "default",
	"offcanvas-style": "default",
	"sidebar": "default"
};
//#endregion
//#region js/tabler-theme.ts
/**
* demo-theme is specifically loaded right after the body and not deferred
* to ensure we switch to the chosen dark/light theme as fast as possible.
* This will prevent any flashes of the light theme (default) before switching.
*/
var params = new URLSearchParams(window.location.search);
var prefersDark = window.matchMedia("(prefers-color-scheme: dark)");
var VALUE_PATTERN = /^[a-z0-9-]{1,32}$/;
var storage = {
	get(key) {
		try {
			return localStorage.getItem(key);
		} catch (_unused) {
			return null;
		}
	},
	set(key, value) {
		try {
			localStorage.setItem(key, value);
		} catch (_unused2) {}
	}
};
for (const key in themeDefaults) {
	const param = params.get(key);
	let selectedValue;
	if (param && VALUE_PATTERN.test(param)) {
		storage.set("tabler-" + key, param);
		selectedValue = param;
	} else {
		var _ref;
		const storedTheme = storage.get("tabler-" + key);
		const serverValue = document.documentElement.getAttribute("data-bs-" + key);
		selectedValue = (_ref = storedTheme !== null && storedTheme !== void 0 ? storedTheme : serverValue) !== null && _ref !== void 0 ? _ref : themeDefaults[key];
	}
	if (key === "theme" && selectedValue === "auto") selectedValue = prefersDark.matches ? "dark" : "light";
	if (selectedValue !== themeDefaults[key]) document.documentElement.setAttribute("data-bs-" + key, selectedValue);
	else document.documentElement.removeAttribute("data-bs-" + key);
}
prefersDark.addEventListener("change", (event) => {
	var _storage$get;
	if (((_storage$get = storage.get("tabler-theme")) !== null && _storage$get !== void 0 ? _storage$get : "auto") === "auto") {
		if (event.matches) document.documentElement.setAttribute("data-bs-theme", "dark");
		else document.documentElement.removeAttribute("data-bs-theme");
	}
});
//#endregion

//# sourceMappingURL=tabler-theme.esm.js.map