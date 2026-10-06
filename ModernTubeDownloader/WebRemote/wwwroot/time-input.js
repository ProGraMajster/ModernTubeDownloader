"use strict";
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.MTDTimeInput = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  const maximumSeconds = 30 * 24 * 60 * 60;

  function seconds(parts) {
    const values = [parts.hours, parts.minutes, parts.seconds].map(value => String(value ?? "").trim());
    if (values.every(value => value === "")) return null;
    if (values.some(value => value !== "" && !/^[0-9]+$/.test(value))) throw new Error("rangeInvalid");
    const [hours, minutes, seconds] = values.map(value => Number(value || 0));
    const total = hours * 3600 + minutes * 60 + seconds;
    if (!Number.isSafeInteger(total) || minutes > 59 || seconds > 59 || total > maximumSeconds)
      throw new Error("rangeInvalid");
    return total;
  }

  function format(total) {
    if (total === null) return "";
    const days = Math.floor(total / 86400);
    const clock = [Math.floor(total % 86400 / 3600), Math.floor(total % 3600 / 60), total % 60]
      .map(value => String(value).padStart(2, "0")).join(":");
    // Match MediaTimeRange.TryParseTime, including recordings longer than 24 hours.
    return days ? `${days}.${clock}` : clock;
  }

  function toProtocolTime(parts) { return format(seconds(parts)); }

  function range(mode, fromParts, toParts) {
    if (mode === "full") return { from: "", to: "" };
    if (mode !== "custom") throw new Error("rangeInvalid");
    const from = seconds(fromParts), to = seconds(toParts);
    if (from === null && to === null) throw new Error("rangeRequired");
    if (to !== null && to <= (from ?? 0)) throw new Error("rangeOrder");
    return { from: format(from), to: format(to) };
  }

  return { toProtocolTime, range };
});
