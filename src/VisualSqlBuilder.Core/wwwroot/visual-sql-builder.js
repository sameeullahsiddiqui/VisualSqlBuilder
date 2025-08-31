window.sqlBuilderInterop = {
    canvasElement: null,
    dotNetHelper: null, 
    interact: null,
    zoom: 1,

    initializeSqlCanvas: function (element, dotNetHelper) {
        console.log('Canvas element received:', element);
        this.canvasElement = element;
        this.dotNetHelper = dotNetHelper; // Store the C# reference

        // Load interact.js
        if (!window.interact) {
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/interactjs/dist/interact.min.js';
            script.onload = () => {
                this.setupInteractions();
                this.setupContextMenu();
            };
            document.head.appendChild(script);
        } else {
            this.setupInteractions();
            this.setupContextMenu();
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
                    move: this.dragMoveListener.bind(this),
                    end: this.dragEndListener.bind(this)
                }
            })
            .resizable({
                edges: { left: true, right: true, bottom: true, top: true },
                listeners: {
                    move: this.resizeMoveListener.bind(this)
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
                    move: this.dragMoveListener.bind(this)
                }
            })
            .resizable({
                edges: { left: true, right: true, bottom: true, top: true },
                listeners: {
                    move: this.resizeMoveListener.bind(this)
                }
            });
    },

    setupContextMenu: function () {
        const contextMenu = document.getElementById('rename-table-menu');
        let currentTableId = null;

        // Listen for right-click on the table cards
        this.canvasElement.addEventListener('contextmenu', (e) => {
            const tableHeader = e.target.closest('.table-card-header');
            if (tableHeader) {
                e.preventDefault(); // Prevent the default browser context menu

                currentTableId = tableHeader.parentElement.getAttribute('data-table-id');

                // Position and show the custom menu
                contextMenu.style.display = 'block';
                contextMenu.style.left = `${e.clientX}px`;
                contextMenu.style.top = `${e.clientY}px`;
            } else {
                contextMenu.style.display = 'none'; // Hide if click is not on a table header
            }
        });

        // Hide the context menu on any left-click
        document.addEventListener('click', () => {
            contextMenu.style.display = 'none';
        });

        // Handle clicks on the menu items
        contextMenu.addEventListener('click', (e) => {
            const action = e.target.getAttribute('data-action');
            if (action === 'rename' && currentTableId) {
                // Find the current name to pass it to the C# modal
                const tableNameElement = document.querySelector(`[data-table-id="${currentTableId}"] .table-card-header-text`);
                const currentName = tableNameElement ? tableNameElement.textContent : '';

                // Call the C# method using the DotNetObjectReference
                this.dotNetHelper.invokeMethodAsync('ShowRenameModal', currentTableId, currentName);
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

        this.updateRelationshipLines();
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
        this.updateRelationshipLines();
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
        const canvas = this.canvasElement;
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

export function initializeSqlCanvas(element, dotNetHelper) {
    window.sqlBuilderInterop.initializeSqlCanvas(element, dotNetHelper);
}

export function setCanvasZoom(zoomLevel) {
    // This calls the internal method on your main object.
    window.sqlBuilderInterop.setCanvasZoom.call(window.sqlBuilderInterop, zoomLevel);
}

export function showModal(modalId) {
    window.sqlBuilderInterop.showModal.call(window.sqlBuilderInterop, modalId);
}

export function hideModal(modalId) {
    window.sqlBuilderInterop.hideModal.call(window.sqlBuilderInterop, modalId);
}

export function saveToLocalStorage(key, value) {
    window.sqlBuilderInterop.saveToLocalStorage.call(window.sqlBuilderInterop, key, value);
}

export function loadFromLocalStorage(key) {
    return window.sqlBuilderInterop.loadFromLocalStorage.call(window.sqlBuilderInterop, key);
}


window.initializeSqlCanvas = initializeSqlCanvas;
window.setCanvasZoom = setCanvasZoom;
window.showModal = showModal;
window.hideModal = hideModal;
window.saveToLocalStorage = saveToLocalStorage;
window.loadFromLocalStorage = loadFromLocalStorage;

// Export for Blazor
// These are correct and work by binding the functions to the correct object context.
//window.initializeSqlCanvas = window.sqlBuilderInterop.initializeSqlCanvas.bind(window.sqlBuilderInterop);
//window.setCanvasZoom = window.sqlBuilderInterop.setCanvasZoom.bind(window.sqlBuilderInterop);
//window.showModal = window.sqlBuilderInterop.showModal.bind(window.sqlBuilderInterop);
//window.hideModal = window.sqlBuilderInterop.hideModal.bind(window.sqlBuilderInterop);
//window.saveToLocalStorage = window.sqlBuilderInterop.saveToLocalStorage.bind(window.sqlBuilderInterop);
//window.loadFromLocalStorage = window.sqlBuilderInterop.loadFromLocalStorage.bind(window.sqlBuilderInterop);