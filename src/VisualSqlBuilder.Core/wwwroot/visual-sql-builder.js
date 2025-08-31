window.sqlBuilderInterop = {
    canvasElement: null,
    interact: null,
    zoom: 1,

    initializeSqlCanvas: function (element) {
        console.log('Canvas element received:', canvasElement);
        this.canvasElement = element;

        // Load interact.js
        if (!window.interact) {
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/interactjs/dist/interact.min.js';
            script.onload = () => {
                this.setupInteractions();
            };
            document.head.appendChild(script);
        } else {
            this.setupInteractions();
        }
    },

    setupInteractions: function () {
        // Make tables draggable
        interact('.table-card')
            .draggable({
                inertia: true,
                modifiers: [
                    interact.modifiers.restrictRect({
                        restriction: 'parent',
                        endOnly: true
                    })
                ],
                autoScroll: true,
                listeners: {
                    move: this.dragMoveListener,
                    end: this.dragEndListener
                }
            })
            .resizable({
                edges: { left: true, right: true, bottom: true, top: true },
                listeners: {
                    move: this.resizeMoveListener
                },
                modifiers: [
                    interact.modifiers.restrictEdges({
                        outer: 'parent'
                    }),
                    interact.modifiers.restrictSize({
                        min: { width: 200, height: 150 }
                    })
                ],
                inertia: true
            });

        // Make column connectors interactive
        this.setupColumnConnectors();

        // Setup domain interactions
        interact('.domain-container')
            .draggable({
                allowFrom: '.domain-header',
                listeners: {
                    move: this.dragMoveListener
                }
            })
            .resizable({
                edges: { left: true, right: true, bottom: true, top: true },
                listeners: {
                    move: this.resizeMoveListener
                }
            });
    },

    dragMoveListener: function (event) {
        const target = event.target;
        const x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx;
        const y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy;

        target.style.transform = `translate(${x}px, ${y}px)`;
        target.setAttribute('data-x', x);
        target.setAttribute('data-y', y);

        // Update relationship lines
        window.sqlBuilderInterop.updateRelationshipLines();
    },

    dragEndListener: function (event) {
        const target = event.target;
        const tableId = target.getAttribute('data-table-id');

        // Notify Blazor of position change
        DotNet.invokeMethodAsync('VisualSqlBuilder.Core', 'UpdateTablePosition',
            tableId,
            parseFloat(target.getAttribute('data-x')) || 0,
            parseFloat(target.getAttribute('data-y')) || 0
        );
    },

    resizeMoveListener: function (event) {
        const target = event.target;
        let { x, y } = target.dataset;

        x = (parseFloat(x) || 0) + event.deltaRect.left;
        y = (parseFloat(y) || 0) + event.deltaRect.top;

        Object.assign(target.style, {
            width: `${event.rect.width}px`,
            height: `${event.rect.height}px`,
            transform: `translate(${x}px, ${y}px)`
        });

        Object.assign(target.dataset, { x, y });

        // Update relationship lines
        window.sqlBuilderInterop.updateRelationshipLines();
    },

    setupColumnConnectors: function () {
        let dragLine = null;
        let startConnector = null;

        document.addEventListener('mousedown', (e) => {
            if (e.target.classList.contains('column-connector')) {
                e.preventDefault();
                startConnector = e.target;

                const rect = startConnector.getBoundingClientRect();
                const canvasRect = this.canvasElement.getBoundingClientRect();

                dragLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                dragLine.setAttribute('stroke', '#0d6efd');
                dragLine.setAttribute('stroke-width', '2');
                dragLine.setAttribute('stroke-dasharray', '5,5');
                dragLine.setAttribute('x1', rect.left - canvasRect.left + 6);
                dragLine.setAttribute('y1', rect.top - canvasRect.top + 6);
                dragLine.setAttribute('x2', rect.left - canvasRect.left + 6);
                dragLine.setAttribute('y2', rect.top - canvasRect.top + 6);

                document.querySelector('.relationship-layer').appendChild(dragLine);
            }
        });

        document.addEventListener('mousemove', (e) => {
            if (dragLine) {
                const canvasRect = this.canvasElement.getBoundingClientRect();
                dragLine.setAttribute('x2', e.clientX - canvasRect.left);
                dragLine.setAttribute('y2', e.clientY - canvasRect.top);
            }
        });

        document.addEventListener('mouseup', (e) => {
            if (dragLine) {
                dragLine.remove();

                if (e.target.classList.contains('column-connector') && e.target !== startConnector) {
                    const sourceTableId = startConnector.getAttribute('data-table-id');
                    const sourceColumnId = startConnector.getAttribute('data-column-id');
                    const targetTableId = e.target.getAttribute('data-table-id');
                    const targetColumnId = e.target.getAttribute('data-column-id');

                    // Create relationship
                    DotNet.invokeMethodAsync('VisualSqlBuilder.Core', 'CreateRelationship',
                        sourceTableId, sourceColumnId, targetTableId, targetColumnId
                    );
                }

                dragLine = null;
                startConnector = null;
            }
        });
    },

    updateRelationshipLines: function () {
        // Recalculate positions of relationship lines based on table positions
        const lines = document.querySelectorAll('.relationship-line');
        lines.forEach(line => {
            // This would be updated by Blazor component
        });
    },

    setCanvasZoom: function (zoom) {
        this.zoom = zoom;
        const canvas = document.getElementById('sql-canvas');
        if (canvas) {
            canvas.style.transform = `scale(${zoom})`;
            canvas.style.transformOrigin = 'top left';
        }
    },

    showModal: function (modalId) {
        const modal = new bootstrap.Modal(document.getElementById(modalId));
        modal.show();
    },

    hideModal: function (modalId) {
        const modal = bootstrap.Modal.getInstance(document.getElementById(modalId));
        if (modal) {
            modal.hide();
        }
    },

    saveToLocalStorage: function (key, value) {
        localStorage.setItem(key, value);
    },

    loadFromLocalStorage: function (key) {
        return localStorage.getItem(key);
    }
};

// Initialize when document is ready
document.addEventListener('DOMContentLoaded', function () {
    if (window.sqlBuilderInterop.canvasElement) {
        window.sqlBuilderInterop.initializeSqlCanvas(window.sqlBuilderInterop.canvasElement);
    }
});

// Export for Blazor
window.initializeSqlCanvas = window.sqlBuilderInterop.initializeSqlCanvas.bind(window.sqlBuilderInterop);
window.setCanvasZoom = window.sqlBuilderInterop.setCanvasZoom.bind(window.sqlBuilderInterop);
window.showModal = window.sqlBuilderInterop.showModal.bind(window.sqlBuilderInterop);
window.hideModal = window.sqlBuilderInterop.hideModal.bind(window.sqlBuilderInterop);
window.saveToLocalStorage = window.sqlBuilderInterop.saveToLocalStorage.bind(window.sqlBuilderInterop);
window.loadFromLocalStorage = window.sqlBuilderInterop.loadFromLocalStorage.bind(window.sqlBuilderInterop);