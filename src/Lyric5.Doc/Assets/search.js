// The search box. Each version carries its own index under pagefind/, written when the site is
// built; where it is there the field turns on and lists what Pagefind finds under the header —
// the arrows move through the results, Enter opens one, Escape closes the list. Without the index,
// or without scripts, the field stays off.
(function () {
  var html = document.documentElement;
  var form = document.querySelector("form.search");
  if (!form) return;
  var input = form.querySelector("input");
  var list = form.querySelector(".search-results");
  if (!input || !list) return;

  var versionRoot = new URL(html.getAttribute("data-root") || "./", location.href);
  var pagefind = null;
  var results = [];
  var active = -1;
  var asked = 0;

  import(new URL("pagefind/pagefind.js", versionRoot).href)
    .then(function (module) {
      pagefind = module;
      return pagefind.options({ baseUrl: versionRoot.pathname });
    })
    .then(function () {
      input.disabled = false;
    })
    .catch(function () { /* no index: the field stays off */ });

  function close() {
    list.hidden = true;
    input.setAttribute("aria-expanded", "false");
    active = -1;
  }

  function mark(index) {
    var items = list.querySelectorAll("li");
    for (var i = 0; i < items.length; i++) items[i].setAttribute("aria-selected", i === index ? "true" : "false");
    active = index;
    if (items[index]) items[index].scrollIntoView({ block: "nearest" });
  }

  function show(found) {
    results = found;
    list.textContent = "";
    if (found.length === 0) {
      var none = document.createElement("li");
      none.className = "none";
      none.textContent = "Nothing found";
      list.appendChild(none);
    }
    found.forEach(function (result, i) {
      var item = document.createElement("li");
      item.setAttribute("role", "option");
      item.id = "search-result-" + i;
      var link = document.createElement("a");
      link.href = result.url;
      var title = document.createElement("span");
      title.className = "title";
      title.textContent = (result.meta && result.meta.title) || result.url;
      link.appendChild(title);
      if (result.meta && result.meta.area) {
        var area = document.createElement("span");
        area.className = "area";
        area.textContent = result.meta.area;
        link.appendChild(area);
      }
      var excerpt = document.createElement("span");
      excerpt.className = "excerpt";
      // Pagefind's excerpt is the page's own text, escaped, with the matches in <mark>.
      excerpt.innerHTML = result.excerpt;
      link.appendChild(excerpt);
      item.appendChild(link);
      list.appendChild(item);
    });
    list.hidden = false;
    input.setAttribute("aria-expanded", "true");
    active = -1;
  }

  input.addEventListener("input", function () {
    var term = input.value.trim();
    if (!pagefind || term.length === 0) { close(); return; }
    // A code written without its prefix, such as SEM0001, is looked up as the code the index holds.
    if (/^[A-Za-z]{2,3}\d{4}$/.test(term)) term = "LYR-" + term.toUpperCase();
    var ask = ++asked;
    pagefind.debouncedSearch(term, {}, 150)
      .then(function (search) {
        if (search === null || ask !== asked) return null;
        return Promise.all(search.results.slice(0, 8).map(function (r) { return r.data(); }));
      })
      .then(function (found) { if (found !== null && ask === asked) show(found); })
      .catch(function () { close(); });
  });

  input.addEventListener("keydown", function (event) {
    if (list.hidden) return;
    if (event.key === "ArrowDown") { event.preventDefault(); mark(Math.min(active + 1, results.length - 1)); }
    else if (event.key === "ArrowUp") { event.preventDefault(); mark(Math.max(active - 1, 0)); }
    else if (event.key === "Escape") { close(); }
  });

  form.addEventListener("submit", function (event) {
    event.preventDefault();
    var chosen = results[active >= 0 ? active : 0];
    if (chosen) location.href = chosen.url;
  });

  document.addEventListener("click", function (event) {
    if (!form.contains(event.target)) close();
  });
})();
