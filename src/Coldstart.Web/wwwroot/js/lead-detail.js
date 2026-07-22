// Send handoff: build Gmail-compose / mailto / clipboard from the current field values,
// so the email is sent from the customer's OWN mail client (their domain), not ours.
(function () {
  "use strict";
  var to = document.getElementById("lead-recipient");
  var subj = document.getElementById("lead-subject");
  var body = document.getElementById("lead-body");
  var gmail = document.getElementById("handoff-gmail");
  var mailto = document.getElementById("handoff-mailto");
  var copy = document.getElementById("handoff-copy");
  var copied = document.getElementById("handoff-copied");
  if (!subj || !body) return;   // not on an approved lead

  function vals() {
    return {
      to: (to && to.value ? to.value : "").trim(),
      su: subj.value || "",
      bd: body.value || ""
    };
  }
  function refresh() {
    var v = vals();
    if (gmail) gmail.href = "https://mail.google.com/mail/?view=cm&fs=1&to=" +
      encodeURIComponent(v.to) + "&su=" + encodeURIComponent(v.su) + "&body=" + encodeURIComponent(v.bd);
    if (mailto) mailto.href = "mailto:" + encodeURIComponent(v.to) +
      "?subject=" + encodeURIComponent(v.su) + "&body=" + encodeURIComponent(v.bd);
  }
  [to, subj, body].forEach(function (el) { if (el) el.addEventListener("input", refresh); });
  refresh();

  if (copy && copied) {
    copy.addEventListener("click", function () {
      var v = vals();
      var text = v.su + "\n\n" + v.bd;
      var done = function () { copied.hidden = false; setTimeout(function () { copied.hidden = true; }, 2000); };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done).catch(done);
      } else { done(); }
    });
  }
})();
