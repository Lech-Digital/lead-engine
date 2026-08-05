// Mobile rail toggle. Desktop never needs JS — the rail is a sticky flex column.
(function () {
  var burger = document.getElementById("rail-burger");
  var rail = document.getElementById("app-rail");
  if (!burger || !rail) return;
  burger.addEventListener("click", function () {
    var open = rail.classList.toggle("open");
    burger.setAttribute("aria-expanded", String(open));
  });
  document.addEventListener("click", function (e) {
    if (rail.classList.contains("open") && !rail.contains(e.target) && e.target !== burger)
      rail.classList.remove("open");
  });
})();
