window.FitbitCharts = (function () {
    const charts = {};

    return {
        render(canvasId, labels, heartRateData, hrvData) {
            if (charts[canvasId]) {
                charts[canvasId].destroy();
            }
            const canvas = document.getElementById(canvasId);
            if (!canvas) return;

            const hasHr  = heartRateData.some(v => v !== null);
            const hasHrv = hrvData.some(v => v !== null);

            const datasets = [];
            if (hasHr) {
                datasets.push({
                    label: 'Resting HR (bpm)',
                    data: heartRateData,
                    borderColor: 'rgb(220, 53, 69)',
                    backgroundColor: 'rgba(220, 53, 69, 0.08)',
                    tension: 0.3,
                    spanGaps: true,
                    yAxisID: 'yHr'
                });
            }
            if (hasHrv) {
                datasets.push({
                    label: 'HRV RMSSD (ms)',
                    data: hrvData,
                    borderColor: 'rgb(13, 110, 253)',
                    backgroundColor: 'rgba(13, 110, 253, 0.08)',
                    tension: 0.3,
                    spanGaps: true,
                    yAxisID: 'yHrv'
                });
            }

            const scales = {};
            if (hasHr) {
                scales.yHr = {
                    type: 'linear',
                    position: 'left',
                    title: { display: true, text: 'HR (bpm)' }
                };
            }
            if (hasHrv) {
                scales.yHrv = {
                    type: 'linear',
                    position: 'right',
                    title: { display: true, text: 'HRV RMSSD (ms)' },
                    grid: { drawOnChartArea: !hasHr }
                };
            }

            charts[canvasId] = new Chart(canvas, {
                type: 'line',
                data: { labels, datasets },
                options: {
                    responsive: true,
                    interaction: { mode: 'index', intersect: false },
                    plugins: { legend: { position: 'top' } },
                    scales
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
