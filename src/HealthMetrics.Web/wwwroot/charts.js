window.HealthCharts = (function () {
    const charts = {};

    function themeColors() {
        const style = getComputedStyle(document.documentElement);
        return {
            text: style.getPropertyValue("--bs-body-color").trim(),
            muted: style.getPropertyValue("--bs-secondary-color").trim(),
            grid: style.getPropertyValue("--bs-border-color").trim(),
            tooltipBackground: style.getPropertyValue("--bs-tertiary-bg").trim(),
            weekBand: style.getPropertyValue("--hm-chart-week-band").trim(),
            weekSeparator: style.getPropertyValue("--hm-chart-week-separator").trim()
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

    function weekRanges(chart, weekStarts) {
        const chartArea = chart.chartArea;
        const xScale = chart.scales?.x;
        const labels = chart.data.labels;
        if (!chartArea
            || !xScale
            || !Array.isArray(weekStarts)
            || !Array.isArray(labels)
            || weekStarts.length === 0
            || weekStarts.length !== labels.length) {
            return [];
        }

        const ranges = [];
        let rangeStart = 0;
        let currentWeek = weekStarts[0];

        for (let index = 1; index <= weekStarts.length; index++) {
            if (index < weekStarts.length && weekStarts[index] === currentWeek) {
                continue;
            }

            const left = rangeStart === 0
                ? chartArea.left
                : (xScale.getPixelForValue(rangeStart - 1) + xScale.getPixelForValue(rangeStart)) / 2;
            const right = index === weekStarts.length
                ? chartArea.right
                : (xScale.getPixelForValue(index - 1) + xScale.getPixelForValue(index)) / 2;
            ranges.push({ left, right });

            rangeStart = index;
            currentWeek = weekStarts[index];
        }

        return ranges;
    }

    const loadWeekBandsPlugin = {
        id: "loadWeekBands",

        beforeDraw(chart, _args, options) {
            const ranges = weekRanges(chart, options?.weekStarts);
            if (ranges.length === 0) return;

            const colors = themeColors();
            const { ctx, chartArea } = chart;
            ctx.save();
            ranges.forEach((range, index) => {
                if (index % 2 === 0) {
                    ctx.fillStyle = colors.weekBand;
                    ctx.fillRect(
                        range.left,
                        chartArea.top,
                        range.right - range.left,
                        chartArea.bottom - chartArea.top);
                }
            });
            ctx.restore();
        },

        afterDraw(chart, _args, options) {
            const ranges = weekRanges(chart, options?.weekStarts);
            if (ranges.length < 2) return;

            const colors = themeColors();
            const { ctx, chartArea } = chart;
            ctx.save();
            ctx.strokeStyle = colors.weekSeparator;
            ctx.lineWidth = 2.5;
            ctx.setLineDash([]);
            ranges.slice(1).forEach(range => {
                ctx.beginPath();
                ctx.moveTo(range.left, chartArea.top);
                ctx.lineTo(range.left, chartArea.bottom);
                ctx.stroke();
            });
            ctx.restore();
        }
    };

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

        renderLoad(
            canvasId,
            labels,
            dailyCardioLoadData,
            cumulativeCardioLoadData,
            targetData,
            manualAcwrData,
            weekStarts) {
            if (charts[canvasId]) {
                charts[canvasId].destroy();
            }
            const canvas = document.getElementById(canvasId);
            if (!canvas) return;

            const hasDailyCardioLoad = dailyCardioLoadData.some(v => v !== null);
            const hasCumulativeCardioLoad = cumulativeCardioLoadData.some(v => v !== null);
            const hasTarget = targetData.some(v => v !== null);
            const hasManualAcwr = manualAcwrData.some(v => v !== null);
            const colors = themeColors();
            const datasets = [];

            if (hasDailyCardioLoad) {
                datasets.push({
                    label: "Daily Cardio Load",
                    data: dailyCardioLoadData,
                    type: "bar",
                    backgroundColor: "rgba(111, 66, 193, 0.32)",
                    borderColor: "rgb(111, 66, 193)",
                    borderWidth: 1,
                    yAxisID: "yLoad"
                });
            }

            if (hasTarget) {
                datasets.push({
                    label: "Weekly target",
                    data: targetData,
                    type: "line",
                    borderColor: "rgb(25, 135, 84)",
                    backgroundColor: "rgba(25, 135, 84, 0.08)",
                    tension: 0.3,
                    spanGaps: true,
                    yAxisID: "yLoad"
                });
            }

            if (hasCumulativeCardioLoad) {
                datasets.push({
                    label: "Weekly cumulative load",
                    data: cumulativeCardioLoadData,
                    type: "line",
                    backgroundColor: "rgba(111, 66, 193, 0.08)",
                    borderColor: "rgb(111, 66, 193)",
                    borderWidth: 3,
                    pointRadius: 3,
                    pointHoverRadius: 5,
                    tension: 0.25,
                    spanGaps: false,
                    yAxisID: "yLoad"
                });
            }

            if (hasManualAcwr) {
                datasets.push({
                    label: "Manual ACWR",
                    data: manualAcwrData,
                    type: "line",
                    borderColor: "rgb(220, 53, 69)",
                    backgroundColor: "rgba(220, 53, 69, 0.08)",
                    tension: 0.3,
                    spanGaps: true,
                    yAxisID: "yAcwr"
                });
            }

            charts[canvasId] = new Chart(canvas, {
                type: "line",
                data: { labels, datasets },
                plugins: [loadWeekBandsPlugin],
                options: {
                    responsive: true,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        loadWeekBands: { weekStarts },
                        legend: {
                            position: "top",
                            labels: {
                                color: colors.text
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
                            title: { display: true, text: "Daily values (weeks start Monday)", color: colors.muted },
                            ticks: { color: colors.muted },
                            grid: { color: colors.grid },
                            border: { color: colors.grid }
                        },
                        yLoad: {
                            type: "linear",
                            position: "left",
                            beginAtZero: true,
                            title: { display: true, text: "Load (daily / cumulative)", color: colors.muted },
                            ticks: { color: colors.muted },
                            grid: { color: colors.grid },
                            border: { color: colors.grid }
                        },
                        yAcwr: {
                            type: "linear",
                            position: "right",
                            beginAtZero: true,
                            title: { display: true, text: "ACWR", color: colors.muted },
                            ticks: { color: colors.muted },
                            grid: { drawOnChartArea: false, color: colors.grid },
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
