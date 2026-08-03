window.HealthCharts = (function () {
    const charts = {};

    function themeColors() {
        const style = getComputedStyle(document.documentElement);
        return {
            text: style.getPropertyValue("--bs-body-color").trim(),
            muted: style.getPropertyValue("--bs-secondary-color").trim(),
            grid: style.getPropertyValue("--bs-border-color").trim(),
            tooltipBackground: style.getPropertyValue("--bs-tertiary-bg").trim()
        };
    }

    function applyTheme(chart) {
        const colors = themeColors();
        chart.options.plugins.legend.labels.color = colors.text;
        chart.options.plugins.tooltip.titleColor = colors.text;
        chart.options.plugins.tooltip.bodyColor = colors.text;
        chart.options.plugins.tooltip.backgroundColor = colors.tooltipBackground;
        chart.options.plugins.tooltip.borderColor = colors.grid;
        chart.options.plugins.tooltip.borderWidth = 1;

        Object.values(chart.options.scales ?? {}).forEach(scale => {
            if (!scale) return;

            scale.title ??= {};
            scale.ticks ??= {};
            scale.grid ??= {};
            scale.border ??= {};
            scale.title.color = colors.muted;
            scale.ticks.color = colors.muted;
            scale.grid.color = colors.grid;
            scale.border.color = colors.grid;
        });

        chart.update();
    }

    window.addEventListener("healthmetrics:themechange", () => {
        Object.values(charts).forEach(applyTheme);
    });

    return {
        render(canvasId, labels, heartRateData, hrvData) {
            if (charts[canvasId]) {
                charts[canvasId].destroy();
            }
            const canvas = document.getElementById(canvasId);
            if (!canvas) return;

            const hasHr = heartRateData.some(v => v !== null);
            const hasHrv = hrvData.some(v => v !== null);
            const datasets = [];

            if (hasHr) {
                datasets.push({
                    label: "Resting HR (bpm)",
                    data: heartRateData,
                    borderColor: "rgb(220, 53, 69)",
                    backgroundColor: "rgba(220, 53, 69, 0.08)",
                    tension: 0.3,
                    spanGaps: true,
                    yAxisID: "yHr"
                });
            }

            if (hasHrv) {
                datasets.push({
                    label: "HRV RMSSD (ms)",
                    data: hrvData,
                    borderColor: "rgb(13, 110, 253)",
                    backgroundColor: "rgba(13, 110, 253, 0.08)",
                    tension: 0.3,
                    spanGaps: true,
                    yAxisID: "yHrv"
                });
            }

            const colors = themeColors();
            const scales = {
                x: {
                    title: { display: false, color: colors.muted },
                    ticks: { color: colors.muted },
                    grid: { color: colors.grid },
                    border: { color: colors.grid }
                }
            };
            if (hasHr) {
                scales.yHr = {
                    type: "linear",
                    position: "left",
                    title: { display: true, text: "HR (bpm)", color: colors.muted },
                    ticks: { color: colors.muted },
                    grid: { color: colors.grid },
                    border: { color: colors.grid }
                };
            }
            if (hasHrv) {
                scales.yHrv = {
                    type: "linear",
                    position: "right",
                    title: { display: true, text: "HRV RMSSD (ms)", color: colors.muted },
                    ticks: { color: colors.muted },
                    grid: { drawOnChartArea: !hasHr, color: colors.grid },
                    border: { color: colors.grid }
                };
            }

            charts[canvasId] = new Chart(canvas, {
                type: "line",
                data: { labels, datasets },
                options: {
                    responsive: true,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: { position: "top", labels: { color: colors.text } },
                        tooltip: {
                            titleColor: colors.text,
                            bodyColor: colors.text,
                            backgroundColor: colors.tooltipBackground,
                            borderColor: colors.grid,
                            borderWidth: 1
                        }
                    },
                    scales
                }
            });
        },

        renderLoad(canvasId, labels, cardioLoadData, targetMinData, targetMaxData) {
            if (charts[canvasId]) {
                charts[canvasId].destroy();
            }
            const canvas = document.getElementById(canvasId);
            if (!canvas) return;

            const hasCardioLoad = cardioLoadData.some(v => v !== null);
            const hasTargetRange = targetMinData.some(v => v !== null) || targetMaxData.some(v => v !== null);
            const colors = themeColors();
            const datasets = [];

            if (hasTargetRange) {
                datasets.push({
                    label: "Local AZM target min",
                    data: targetMinData,
                    type: "line",
                    borderColor: "rgba(25, 135, 84, 0)",
                    backgroundColor: "rgba(25, 135, 84, 0)",
                    pointRadius: 0,
                    borderWidth: 0,
                    fill: false,
                    spanGaps: true,
                    yAxisID: "yLoad"
                });
                datasets.push({
                    label: "Local AZM target range",
                    data: targetMaxData,
                    type: "line",
                    borderColor: "rgba(25, 135, 84, 0.7)",
                    backgroundColor: "rgba(25, 135, 84, 0.16)",
                    pointRadius: 0,
                    borderWidth: 1,
                    fill: "-1",
                    spanGaps: true,
                    yAxisID: "yLoad"
                });
            }

            if (hasCardioLoad) {
                datasets.push({
                    label: "Active Zone Minutes (AZM)",
                    data: cardioLoadData,
                    type: "bar",
                    backgroundColor: "rgba(13, 110, 253, 0.62)",
                    borderColor: "rgb(13, 110, 253)",
                    borderWidth: 1,
                    yAxisID: "yLoad"
                });
            }

            charts[canvasId] = new Chart(canvas, {
                type: "bar",
                data: { labels, datasets },
                options: {
                    responsive: true,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: {
                            position: "top",
                            labels: {
                                color: colors.text,
                                filter: item => item.text !== "Local AZM target min"
                            }
                        },
                        tooltip: {
                            titleColor: colors.text,
                            bodyColor: colors.text,
                            backgroundColor: colors.tooltipBackground,
                            borderColor: colors.grid,
                            borderWidth: 1
                        }
                    },
                    scales: {
                        x: {
                            title: { display: false, color: colors.muted },
                            ticks: { color: colors.muted },
                            grid: { color: colors.grid },
                            border: { color: colors.grid }
                        },
                        yLoad: {
                            type: "linear",
                            position: "left",
                            beginAtZero: true,
                            title: { display: true, text: "Active Zone Minutes (AZM)", color: colors.muted },
                            ticks: { color: colors.muted },
                            grid: { color: colors.grid },
                            border: { color: colors.grid }
                        }
                    }
                }
            });
        },

        destroy(canvasId) {
            if (charts[canvasId]) {
                charts[canvasId].destroy();
                delete charts[canvasId];
            }
        }
    };
})();
