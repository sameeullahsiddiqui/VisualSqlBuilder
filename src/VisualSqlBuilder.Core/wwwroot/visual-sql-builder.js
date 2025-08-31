window.sqlBuilderInterop = {
    canvasElement: null,
    dotNetHelper: null,
    interact: null,
    zoom: 1,
    tableInteractionState: null, // Track if table is being dragged/resized
    dragState: {
        isDragging: false,
        dragLine: null,
        startConnector: null,
        hoveredConnector: null
    },

    initializeSqlCanvas: function (element, dotNetHelper) {
        console.log('Canvas element received:', element);
        this.canvasElement = element;
        this.dotNetHelper = dotNetHelper;

        // Load interact.js
        if (!window.interact) {
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/interactjs/dist/interact.min.js';
            script.onload = () => {
                this.setupInteractions();
                this.setupContextMenu();
                this.setupRelationshipCreation();
            };
            document.head.appendChild(script);
        } else {
            this.setupInteractions();
            this.setupContextMenu();
            this.setupRelationshipCreation();
        }
    },

    setupInteractions: function () {
        // Make tables draggable - but prevent dragging when starting from column connectors
        interact('.table-card')
            .draggable({
                inertia: true,
                // Prevent dragging when starting from column connectors or other specific elements
                ignoreFrom: '.column-connector, .btn, .form-check-input, .column-row',
                // Only allow dragging from the header area
                allowFrom: '.table-card-header',
                modifiers: [
                    interact.modifiers.restrictRect({
                        restriction: 'parent',
                        endOnly: true
                    })
                ],
                autoScroll: true,
                listeners: {
                    start: this.tableDragStart.bind(this),
                    move: this.dragMoveListener.bind(this),
                    end: this.dragEndListener.bind(this)
                }
            })
            .resizable({
                edges: { left: true, right: true, bottom: true, top: true },
                // Prevent resizing when interacting with column connectors
                ignoreFrom: '.column-connector',
                listeners: {
                    start: this.tableResizeStart.bind(this),
                    move: this.resizeMoveListener.bind(this),
                    end: this.tableResizeEnd.bind(this)
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
        const relationshipContextMenu = document.getElementById('relationship-context-menu');
        let currentTableId = null;
        let currentRelationshipId = null;

        // Table context menu
        this.canvasElement.addEventListener('contextmenu', (e) => {
            const tableHeader = e.target.closest('.table-card-header');
            const relationshipLine = e.target.closest('.relationship-line');

            if (tableHeader) {
                e.preventDefault();
                currentTableId = tableHeader.parentElement.getAttribute('data-table-id');
                contextMenu.style.display = 'block';
                contextMenu.style.left = `${e.clientX}px`;
                contextMenu.style.top = `${e.clientY}px`;
                if (relationshipContextMenu) relationshipContextMenu.style.display = 'none';
            } else if (relationshipLine) {
                e.preventDefault();
                currentRelationshipId = relationshipLine.getAttribute('data-relationship-id');
                if (relationshipContextMenu) {
                    relationshipContextMenu.style.display = 'block';
                    relationshipContextMenu.style.left = `${e.clientX}px`;
                    relationshipContextMenu.style.top = `${e.clientY}px`;
                }
                contextMenu.style.display = 'none';
            } else {
                contextMenu.style.display = 'none';
                if (relationshipContextMenu) relationshipContextMenu.style.display = 'none';
            }
        });

        // Hide context menus on click
        document.addEventListener('click', () => {
            contextMenu.style.display = 'none';
            if (relationshipContextMenu) relationshipContextMenu.style.display = 'none';
        });

        // Handle table context menu actions
        contextMenu.addEventListener('click', (e) => {
            const action = e.target.getAttribute('data-action');
            if (action === 'rename' && currentTableId) {
                const tableNameElement = document.querySelector(`[data-table-id="${currentTableId}"] .table-card-header-text`);
                const currentName = tableNameElement ? tableNameElement.textContent : '';
                this.dotNetHelper.invokeMethodAsync('ShowRenameModal', currentTableId, currentName);
            }
        });

        // Handle relationship context menu actions
        if (relationshipContextMenu) {
            relationshipContextMenu.addEventListener('click', (e) => {
                const action = e.target.getAttribute('data-action');
                if (currentRelationshipId) {
                    switch (action) {
                        case 'edit-join':
                            this.dotNetHelper.invokeMethodAsync('ShowJoinTypeModal', currentRelationshipId);
                            break;
                        case 'delete-relationship':
                            this.dotNetHelper.invokeMethodAsync('DeleteRelationship', currentRelationshipId);
                            break;
                    }
                }
            });
        }
    },

    setupRelationshipCreation: function () {
        // Enhanced column connector setup with better isolation from table interactions
        document.addEventListener('mousedown', (e) => {
            if (e.target.classList.contains('column-connector')) {
                e.preventDefault();
                e.stopPropagation(); // Prevent event bubbling to table
                this.startRelationshipDrag(e);
            }
        }, true); // Use capture phase to intercept before table handlers

        document.addEventListener('mousemove', (e) => {
            if (this.dragState.isDragging) {
                e.preventDefault();
                e.stopPropagation();
                this.updateDragLine(e);
            }
            this.handleConnectorHover(e);
        });

        document.addEventListener('mouseup', (e) => {
            if (this.dragState.isDragging) {
                e.preventDefault();
                e.stopPropagation();
                this.finishRelationshipDrag(e);
            }
        });

        // Keyboard support for relationship creation
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.dragState.isDragging) {
                this.cancelRelationshipDrag();
            }
        });

        // Prevent context menu on column connectors during drag
        document.addEventListener('contextmenu', (e) => {
            if (this.dragState.isDragging || e.target.classList.contains('column-connector')) {
                if (this.dragState.isDragging) {
                    e.preventDefault();
                }
            }
        });
    },

    tableDragStart: function (event) {
        // Disable relationship creation while table is being dragged
        this.tableInteractionState = 'dragging';
        console.log('Table drag started');
    },

    tableResizeStart: function (event) {
        // Disable relationship creation while table is being resized
        this.tableInteractionState = 'resizing';
        console.log('Table resize started');
    },

    tableResizeEnd: function (event) {
        // Re-enable relationship creation after resize
        this.tableInteractionState = null;
        console.log('Table resize ended');
    },

    startRelationshipDrag: function (e) {
        // Don't start relationship drag if table is being interacted with
        if (this.tableInteractionState) {
            console.log('Relationship drag blocked - table interaction in progress:', this.tableInteractionState);
            return;
        }

        // Disable table interactions while creating relationship
        this.disableTableInteractions();

        this.dragState.isDragging = true;
        this.dragState.startConnector = e.target;

        // Add visual feedback to start connector
        e.target.classList.add('connector-active');

        const rect = e.target.getBoundingClientRect();
        const canvasRect = this.canvasElement.getBoundingClientRect();

        // Create drag line
        this.dragState.dragLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        this.dragState.dragLine.setAttribute('stroke', '#0d6efd');
        this.dragState.dragLine.setAttribute('stroke-width', '3');
        this.dragState.dragLine.setAttribute('stroke-dasharray', '8,4');
        this.dragState.dragLine.setAttribute('stroke-linecap', 'round');
        this.dragState.dragLine.setAttribute('class', 'drag-line');

        const startX = rect.left - canvasRect.left + (rect.width / 2);
        const startY = rect.top - canvasRect.top + (rect.height / 2);

        this.dragState.dragLine.setAttribute('x1', startX);
        this.dragState.dragLine.setAttribute('y1', startY);
        this.dragState.dragLine.setAttribute('x2', startX);
        this.dragState.dragLine.setAttribute('y2', startY);

        const relationshipLayer = document.querySelector('.relationship-layer');
        if (relationshipLayer) {
            relationshipLayer.appendChild(this.dragState.dragLine);
        }

        // Add cursor feedback
        document.body.style.cursor = 'crosshair';

        // Highlight potential target connectors
        this.highlightPotentialTargets(this.dragState.startConnector);

        console.log('Relationship drag started from connector:', e.target);
    },

    disableTableInteractions: function () {
        // Temporarily disable table dragging and resizing
        const tableCards = document.querySelectorAll('.table-card');
        tableCards.forEach(table => {
            table.style.pointerEvents = 'none';
        });

        // Re-enable pointer events on column connectors
        const connectors = document.querySelectorAll('.column-connector');
        connectors.forEach(connector => {
            connector.style.pointerEvents = 'all';
        });
    },

    enableTableInteractions: function () {
        // Re-enable table interactions
        const tableCards = document.querySelectorAll('.table-card');
        tableCards.forEach(table => {
            table.style.pointerEvents = '';
        });
    },

    updateDragLine: function (e) {
        if (!this.dragState.dragLine) return;

        const canvasRect = this.canvasElement.getBoundingClientRect();
        const mouseX = e.clientX - canvasRect.left;
        const mouseY = e.clientY - canvasRect.top;

        this.dragState.dragLine.setAttribute('x2', mouseX);
        this.dragState.dragLine.setAttribute('y2', mouseY);

        // Add animation to the dash pattern
        const currentOffset = parseFloat(this.dragState.dragLine.style.strokeDashoffset || 0);
        this.dragState.dragLine.style.strokeDashoffset = (currentOffset - 1) % 12;
    },

    handleConnectorHover: function (e) {
        if (!this.dragState.isDragging) return;

        const connector = e.target.closest('.column-connector');

        // Remove previous hover state
        if (this.dragState.hoveredConnector && this.dragState.hoveredConnector !== connector) {
            this.dragState.hoveredConnector.classList.remove('connector-hover');
        }

        // Add hover state to valid targets
        if (connector && connector !== this.dragState.startConnector && this.isValidTarget(connector)) {
            connector.classList.add('connector-hover');
            this.dragState.hoveredConnector = connector;

            // Change drag line color to indicate valid drop
            if (this.dragState.dragLine) {
                this.dragState.dragLine.setAttribute('stroke', '#28a745');
            }
        } else {
            this.dragState.hoveredConnector = null;
            if (this.dragState.dragLine) {
                this.dragState.dragLine.setAttribute('stroke', '#0d6efd');
            }
        }
    },

    finishRelationshipDrag: function (e) {
        const targetConnector = e.target.closest('.column-connector');

        if (targetConnector && targetConnector !== this.dragState.startConnector && this.isValidTarget(targetConnector)) {
            // Show join type selection modal
            this.showJoinTypeSelection(this.dragState.startConnector, targetConnector);
        }

        this.cancelRelationshipDrag();
    },

    cancelRelationshipDrag: function () {
        // Clean up drag state
        if (this.dragState.dragLine) {
            this.dragState.dragLine.remove();
        }

        if (this.dragState.startConnector) {
            this.dragState.startConnector.classList.remove('connector-active');
        }

        if (this.dragState.hoveredConnector) {
            this.dragState.hoveredConnector.classList.remove('connector-hover');
        }

        // Remove highlights from all connectors
        document.querySelectorAll('.column-connector').forEach(connector => {
            connector.classList.remove('connector-highlight', 'connector-active', 'connector-hover');
        });

        // Reset cursor
        document.body.style.cursor = '';

        // Re-enable table interactions
        this.enableTableInteractions();

        // Reset drag state
        this.dragState = {
            isDragging: false,
            dragLine: null,
            startConnector: null,
            hoveredConnector: null
        };

        console.log('Relationship drag cancelled');
    },

    highlightPotentialTargets: function (startConnector) {
        const startTableId = startConnector.getAttribute('data-table-id');

        document.querySelectorAll('.column-connector').forEach(connector => {
            const tableId = connector.getAttribute('data-table-id');
            if (tableId !== startTableId) {
                connector.classList.add('connector-highlight');
            }
        });
    },

    isValidTarget: function (targetConnector) {
        const startTableId = this.dragState.startConnector.getAttribute('data-table-id');
        const targetTableId = targetConnector.getAttribute('data-table-id');
        return startTableId !== targetTableId;
    },

    showJoinTypeSelection: function (sourceConnector, targetConnector) {
        const sourceTableId = sourceConnector.getAttribute('data-table-id');
        const sourceColumnId = sourceConnector.getAttribute('data-column-id');
        const targetTableId = targetConnector.getAttribute('data-table-id');
        const targetColumnId = targetConnector.getAttribute('data-column-id');

        // Call Blazor to show join type selection modal
        this.dotNetHelper.invokeMethodAsync('ShowJoinTypeSelection',
            sourceTableId, sourceColumnId, targetTableId, targetColumnId);
    },

    // Rest of the existing methods remain the same
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

        // Reset table interaction state
        this.tableInteractionState = null;

        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('UpdateTablePosition',
                tableId,
                parseFloat(target.getAttribute('data-x')) || 0,
                parseFloat(target.getAttribute('data-y')) || 0
            );
        }

        console.log('Table drag ended');
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
        this.updateRelationshipLines();
    },

    updateRelationshipLines: function () {
        // This will be called by Blazor component when tables move
        const lines = document.querySelectorAll('.relationship-line');
        lines.forEach(line => {
            // Update line positions based on table movements
            // This logic would be handled by the Blazor component
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

// Export functions
export function initializeSqlCanvas(element, dotNetHelper) {
    window.sqlBuilderInterop.initializeSqlCanvas(element, dotNetHelper);
}

export function setCanvasZoom(zoomLevel) {
    window.sqlBuilderInterop.setCanvasZoom(zoomLevel);
}

export function showModal(modalId) {
    window.sqlBuilderInterop.showModal(modalId);
}

export function hideModal(modalId) {
    window.sqlBuilderInterop.hideModal(modalId);
}

export function saveToLocalStorage(key, value) {
    window.sqlBuilderInterop.saveToLocalStorage(key, value);
}

export function loadFromLocalStorage(key) {
    return window.sqlBuilderInterop.loadFromLocalStorage(key);
}

// Make functions available globally
window.initializeSqlCanvas = initializeSqlCanvas;
window.setCanvasZoom = setCanvasZoom;
window.showModal = showModal;
window.hideModal = hideModal;
window.saveToLocalStorage = saveToLocalStorage;
window.loadFromLocalStorage = loadFromLocalStorage;