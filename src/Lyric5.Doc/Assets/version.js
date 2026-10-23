// The version switcher. The site's versions are listed beside the version directories, in
// versions.json: [{ "name": "dev", "path": "dev/" }, …], each path relative to the site root.
// Without the list, or without scripts, the header shows this version's name alone.
(function () {
  var root = document.documentElement;
  var base = (root.getAttribute("data-root") || "") + "../";
  var current = root.getAttribute("data-version");
  var label = document.getElementById("version");
  if (!label || !window.fetch) return;
  fetch(base + "versions.json")
    .then(function (response) {
      if (!response.ok) throw new Error("no version list");
      return response.json();
    })
    .then(function (versions) {
      if (!Array.isArray(versions) || versions.length === 0) return;
      var select = document.createElement("select");
      select.className = "version";
      select.setAttribute("aria-label", "Version");
      versions.forEach(function (v) {
        var option = document.createElement("option");
        option.value = v.path;
        option.textContent = v.name;
        if (v.name === current) option.selected = true;
        select.appendChild(option);
      });
      select.addEventListener("change", function () { location.href = base + select.value; });
      label.replaceWith(select);
    })
    .catch(function () { /* the name alone stays */ });
})();
