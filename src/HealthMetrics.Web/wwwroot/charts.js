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

    function visibleIndexBounds(chart, weekStarts) {
        const xScale = chart.scales?.x;
        const labels = chart.data.labels;
        if (!xScale
            || !Array.isArray(weekStarts)
            || !Array.isArray(labels)
            || weekStarts.length !== labels.length
            || labels.length === 0) {
            return null;
        }

        const min = Number(xScale.min);
        const max = Number(xScale.max);
        return {
            first: Math.max(0, Number.isFinite(min) ? Math.floor(min) : 0),
            last: Math.min(labels.length - 1, Number.isFinite(max) ? Math.ceil(max) : labels.length - 1)
        };
    }

    function weekRanges(chart, weekStarts) {
        const chartArea = chart.chartArea;
        const xScale = chart.scales?.x;
        const labels = chart.data.labels;
        const bounds = visibleIndexBounds(chart, weekStarts);
        if (!chartArea || !xScale || !bounds || bounds.first > bounds.last) {
            return [];
        }

        const ranges = [];
        let rangeStart = bounds.first;
        let currentWeek = weekStarts[rangeStart];

        for (let index = bounds.first + 1; index <= bounds.last + 1; index++) {
            if (index <= bounds.last && weekStarts[index] === currentWeek) {
                continue;
            }

            const left = rangeStart === bounds.first
                ? chartArea.left
                : (xScale.getPixelForValue(rangeStart - 1) + xScale.getPixelForValue(rangeStart)) / 2;
            const right = index > bounds.last
                ? chartArea.right
                : (xScale.getPixelForValue(index - 1) + xScale.getPixelForValue(index)) / 2;
            ranges.push({ left, right });

            if (index <= bounds.last) {
                rangeStart = index;
                currentWeek = weekStarts[index];
            }
        }

        return ranges;
    }

    function isWeekBoundary(segmentContext, weekStarts) {
        if (!Array.isArray(weekStarts)) return false;

        const previousWeek = weekStarts[segmentContext.p0DataIndex];
        const currentWeek = weekStarts[segmentContext.p1DataIndex];
        return previousWeek !== undefined
            && currentWeek !== undefined
            && previousWeek !== currentWeek;
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

    function destroyChart(canvasId) {
        const state = charts[canvasId];
        if (!state) return;

        state.cleanup();
        state.chart.destroy();
        delete charts[canvasId];
    }

    function bindHistoryScroll(canvasId, scrollId, chart, visibleDayCount) {
        const scrollElement = document.getElementById(scrollId);
        const spacer = scrollElement?.querySelector(".chart-history-scroll-spacer");
        const totalDays = chart.data.labels.length;
        const windowDays = Math.min(
            totalDays,
            Math.max(1, Number.isFinite(Number(visibleDayCount)) ? Number(visibleDayCount) : totalDays));

        if (!scrollElement || !spacer || totalDays === 0) {
            return () => { };
        }

        let animationFrame = 0;
        let resizeFrame = 0;

        const updateChartWindow = () => {
            animationFrame = 0;
            const maxScroll = scrollElement.scrollWidth - scrollElement.clientWidth;
            const maxStart = Math.max(0, totalDays - windowDays);
            const ratio = maxScroll > 0
                ? scrollElement.scrollLeft / maxScroll
                : 1;
            const start = Math.round(Math.max(0, Math.min(1, ratio)) * maxStart);
            chart.options.scales.x.min = start;
            chart.options.scales.x.max = start + windowDays - 1;
            chart.update("none");
        };

        const scheduleChartWindowUpdate = () => {
            if (animationFrame) return;
            animationFrame = requestAnimationFrame(updateChartWindow);
        };

        const sizeScroller = (showLatest) => {
            resizeFrame = 0;
            const viewportWidth = Math.max(scrollElement.clientWidth, 1);
            const hasOlderHistory = totalDays > windowDays;
            const contentWidth = hasOlderHistory
                ? Math.ceil(viewportWidth * totalDays / windowDays)
                : viewportWidth;
            spacer.style.width = `${contentWidth}px`;
            scrollElement.classList.toggle("has-older-history", hasOlderHistory);
            if (showLatest) {
                scrollElement.scrollLeft = Math.max(0, scrollElement.scrollWidth - viewportWidth);
            }
            updateChartWindow();
        };

        const scheduleScrollerResize = () => {
            if (resizeFrame) return;
            resizeFrame = requestAnimationFrame(() => sizeScroller(false));
        };

        scrollElement.addEventListener("scroll", scheduleChartWindowUpdate, { passive: true });
        window.addEventListener("resize", scheduleScrollerResize);
        requestAnimationFrame(() => sizeScroller(true));

        return () => {
            scrollElement.removeEventListener("scroll", scheduleChartWindowUpdate);
            window.removeEventListener("resize", scheduleScrollerResize);
            if (animationFrame) cancelAnimationFrame(animationFrame);
            if (resizeFrame) cancelAnimationFrame(resizeFrame);
        };
    }

    window.addEventListener("healthmetrics:themechange", () => {
        Object.values(charts).forEach(state => applyTheme(state.chart));
    });

    return {
        render(canvasId, labels, heartRateData, hrvData, visibleDayCount, scrollId) {
            destroyChart(canvasId);
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
                    min: 0,
                    max: Math.max(0, Math.min(labels.length, visibleDayCount) - 1),
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

            const chart = new Chart(canvas, {
                type: "line",
                data: { labels, datasets },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
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
            charts[canvasId] = {
                chart,
                cleanup: () => { }
            };
            charts[canvasId].cleanup = bindHistoryScroll(canvasId, scrollId, chart, visibleDayCount);
        },

        renderLoad(
            canvasId,
            labels,
            dailyCardioLoadData,
            cumulativeCardioLoadData,
            targetData,
            manualAcwrData,
            weekStarts,
            visibleDayCount,
            scrollId) {
            destroyChart(canvasId);
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
                    segment: {
                        borderColor: context =>
                            isWeekBoundary(context, weekStarts)
                                ? "rgba(111, 66, 193, 0)"
                                : "rgb(111, 66, 193)"
                    },
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

            const chart = new Chart(canvas, {
                type: "line",
                data: { labels, datasets },
                plugins: [loadWeekBandsPlugin],
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
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
                            min: 0,
                            max: Math.max(0, Math.min(labels.length, visibleDayCount) - 1),
                            title: { display: true, text: "Daily values (Monday-starting weeks)", color: colors.muted },
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
            charts[canvasId] = {
                chart,
                cleanup: () => { }
            };
            charts[canvasId].cleanup = bindHistoryScroll(canvasId, scrollId, chart, visibleDayCount);
        },

        destroy(canvasId) {
            destroyChart(canvasId);
        },

        resetScroll(elementId) {
            const element = document.getElementById(elementId);
            if (!element) return;

            element.scrollTop = 0;
            element.scrollLeft = 0;
        }
    };
})();
