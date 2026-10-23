// On a narrow window the side navigation is a bar; its button opens the links as a box. Without
// this script the box stays open.
(function () {
  var nav = document.querySelector(".sidebar");
  if (!nav) return;
  var button = nav.querySelector(".contents-toggle");
  if (!button) return;
  button.hidden = false;
  button.addEventListener("click", function () {
    var open = nav.classList.toggle("open");
    button.setAttribute("aria-expanded", open ? "true" : "false");
  });
})();
