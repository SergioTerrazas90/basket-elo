window.basketEloFixtures = {
    getTimeZoneId: function () {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
    },
    getLocalDateTimeValue: function () {
        const now = new Date();
        now.setSeconds(0, 0);
        const offsetMilliseconds = now.getTimezoneOffset() * 60 * 1000;
        return new Date(now.getTime() - offsetMilliseconds).toISOString().slice(0, 16);
    }
};
