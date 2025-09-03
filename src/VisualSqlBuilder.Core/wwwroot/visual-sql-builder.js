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
                this.initializeDomainCollapse(); 
            };
            document.head.appendChild(script);
        } else {
            this.setupInteractions();
            this.setupContextMenu();
            this.setupRelationshipCreation();
            this.initializeDomainCollapse();
        }
    },

    setupContextMenu: function () {
        const contextMenu = document.getElementById('rename-table-menu');
        const relationshipContextMenu = document.getElementById('relationship-context-menu');
        const columnContextMenu = document.getElementById('column-context-menu');
        let currentTableId = null;
        let currentRelationshipId = null;
        let currentColumnId = null;
        let currentColumnTableId = null;

        // Context menu handler
        this.canvasElement.addEventListener('contextmenu', (e) => {
            const tableHeader = e.target.closest('.table-card-header');
            const relationshipLine = e.target.closest('.relationship-line');
            const columnName = e.target.closest('.column-name');

            // Hide all context menus first
            [contextMenu, relationshipContextMenu, columnContextMenu].forEach(menu => {
                if (menu) menu.style.display = 'none';
            });

            if (tableHeader) {
                e.preventDefault();
                currentTableId = tableHeader.parentElement.getAttribute('data-table-id');
                contextMenu.style.display = 'block';
                contextMenu.style.left = `${e.clientX}px`;
                contextMenu.style.top = `${e.clientY}px`;
            } else if (relationshipLine) {
                e.preventDefault();
                currentRelationshipId = relationshipLine.getAttribute('data-relationship-id');
                if (relationshipContextMenu) {
                    relationshipContextMenu.style.display = 'block';
                    relationshipContextMenu.style.left = `${e.clientX}px`;
                    relationshipContextMenu.style.top = `${e.clientY}px`;
                }
            } else if (columnName) {
                e.preventDefault();
                const columnRow = columnName.closest('.column-row');
                currentColumnId = columnRow.querySelector('.column-connector').getAttribute('data-column-id');
                currentColumnTableId = columnRow.querySelector('.column-connector').getAttribute('data-table-id');
                if (columnContextMenu) {
                    columnContextMenu.style.display = 'block';
                    columnContextMenu.style.left = `${e.clientX}px`;
                    columnContextMenu.style.top = `${e.clientY}px`;
                }
            }
        });

        // Hide context menus on click
        document.addEventListener('click', () => {
            [contextMenu, relationshipContextMenu, columnContextMenu].forEach(menu => {
                if (menu) menu.style.display = 'none';
            });
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

        // Handle column context menu actions
        if (columnContextMenu) {
            columnContextMenu.addEventListener('click', (e) => {
                const action = e.target.getAttribute('data-action');
                if (currentColumnId && currentColumnTableId) {
                    switch (action) {
                        case 'set-alias':
                            this.dotNetHelper.invokeMethodAsync('ShowColumnAliasModal', currentColumnTableId, currentColumnId);
                            break;
                        case 'toggle-select':
                            this.dotNetHelper.invokeMethodAsync('ToggleColumnSelection', currentColumnTableId, currentColumnId);
                            break;
                    }
                }
            });
        }
    },

    // Auto-layout tables in a clean arrangement
    autoArrangeTables: function () {
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('AutoArrangeTables');
        }
    },

    autoResizeTable: function (tableElement) {
        if (!tableElement) return;


        const columnCount = tableElement.querySelectorAll('.column-row').length;


        // Define min/max width per column
        const baseWidth = 150; // px per column
        const minWidth = 200;
        const maxWidth = 800;


        const newWidth = Math.min(Math.max(columnCount * baseWidth, minWidth), maxWidth);
        tableElement.style.width = `${newWidth}px`;
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

        const startPos = this.getConnectorPosition(e.target);

        // Create drag line
        this.dragState.dragLine = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        this.dragState.dragLine.setAttribute('stroke', '#0d6efd');
        this.dragState.dragLine.setAttribute('stroke-width', '3');
        this.dragState.dragLine.setAttribute('stroke-dasharray', '8,4');
        this.dragState.dragLine.setAttribute('stroke-linecap', 'round');
        this.dragState.dragLine.setAttribute('class', 'drag-line');

        this.dragState.dragLine.setAttribute('x1', startPos.x);
        this.dragState.dragLine.setAttribute('y1', startPos.y);
        this.dragState.dragLine.setAttribute('x2', startPos.x);
        this.dragState.dragLine.setAttribute('y2', startPos.y);

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
        let x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx;
        let y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy;

        // Check for collision with other tables
        if (this.checkTableCollision(target, x, y)) {
            // Find nearest non-colliding position
            const adjustedPos = this.findNearestNonCollidingPosition(target, x, y);
            x = adjustedPos.x;
            y = adjustedPos.y;
        }

        // Check domain constraints
        const constrainedPos = this.constrainTableToDomain(target, x, y);
        x = constrainedPos.x;
        y = constrainedPos.y;

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

    // Refresh all relationship line positions
    refreshAllRelationshipLines: function () {
        const lines = document.querySelectorAll('.relationship-line');
        lines.forEach(line => {
            const relationshipId = line.getAttribute('data-relationship-id');
            this.updateRelationshipLinePosition(relationshipId);
        });
    },

    // Find nearest position where table doesn't collide
    findNearestNonCollidingPosition: function (element, targetX, targetY) {
        const step = 10;
        const maxAttempts = 20;

        for (let attempt = 0; attempt < maxAttempts; attempt++) {
            const radius = attempt * step;

            // Try positions in a spiral pattern
            for (let angle = 0; angle < 360; angle += 45) {
                const radians = angle * Math.PI / 180;
                const testX = targetX + Math.cos(radians) * radius;
                const testY = targetY + Math.sin(radians) * radius;

                if (!this.checkTableCollision(element, testX, testY)) {
                    return { x: testX, y: testY };
                }
            }
        }

        // If no position found, return original position
        return { x: targetX, y: targetY };
    },

    //Check if two rectangles overlap
    rectanglesOverlap: function (rect1, rect2) {
        const buffer = 20; // Add buffer space between tables

        return !(rect1.x + rect1.width + buffer < rect2.x ||
            rect2.x + rect2.width + buffer < rect1.x ||
            rect1.y + rect1.height + buffer < rect2.y ||
            rect2.y + rect2.height + buffer < rect1.y);
    },

    // Check for table collisions during drag
    checkTableCollision: function (draggedElement, newX, newY) {
        const draggedRect = {
            x: newX,
            y: newY,
            width: draggedElement.offsetWidth,
            height: draggedElement.offsetHeight
        };

        const tables = document.querySelectorAll('.table-card');

        for (let table of tables) {
            if (table === draggedElement) continue;

            const tableRect = {
                x: parseFloat(table.getAttribute('data-x')) || 0,
                y: parseFloat(table.getAttribute('data-y')) || 0,
                width: table.offsetWidth,
                height: table.offsetHeight
            };

            if (this.rectanglesOverlap(draggedRect, tableRect)) {
                return true;
            }
        }

        return false;
    },

    // Get precise connector position relative to canvas
    getConnectorPosition: function (connector) {
        const rect = connector.getBoundingClientRect();
        const canvasRect = this.canvasElement.getBoundingClientRect();

        return {
            x: rect.left - canvasRect.left + (rect.width / 2),
            y: rect.top - canvasRect.top + (rect.height / 2)
        };
    },

    // Calculate connection points with smart routing
    calculateConnectionPath: function (sourceConnector, targetConnector) {
        const sourcePos = this.getConnectorPosition(sourceConnector);
        const targetPos = this.getConnectorPosition(targetConnector);

        // Smart routing to avoid overlapping tables
        const tables = Array.from(document.querySelectorAll('.table-card'));
        const path = this.findOptimalPath(sourcePos, targetPos, tables);

        return path;
    },

    // Find optimal path avoiding table overlaps
    findOptimalPath: function (start, end, obstacles) {
        // Simple orthogonal routing with obstacle avoidance
        const midX = (start.x + end.x) / 2;
        const midY = (start.y + end.y) / 2;

        // Check for obstacles and adjust path
        const hasObstacle = this.checkPathForObstacles(start, end, obstacles);

        if (hasObstacle) {
            // Route around obstacles using waypoints
            const waypoints = this.calculateWaypoints(start, end, obstacles);
            return waypoints;
        }

        return [start, end];
    },

    // Check if direct path intersects with tables
    checkPathForObstacles: function (start, end, tables) {
        for (let table of tables) {
            const rect = table.getBoundingClientRect();
            const canvasRect = this.canvasElement.getBoundingClientRect();

            const tableRect = {
                x: rect.left - canvasRect.left,
                y: rect.top - canvasRect.top,
                width: rect.width,
                height: rect.height
            };

            if (this.lineIntersectsRect(start, end, tableRect)) {
                return true;
            }
        }
        return false;
    },

    // Calculate waypoints to route around obstacles
    calculateWaypoints: function (start, end, obstacles) {
        // Simple L-shaped routing
        const horizontal = Math.abs(end.x - start.x) > Math.abs(end.y - start.y);

        if (horizontal) {
            const midPoint = { x: end.x, y: start.y };
            return [start, midPoint, end];
        } else {
            const midPoint = { x: start.x, y: end.y };
            return [start, midPoint, end];
        }
    },

    // Check if line intersects rectangle
    lineIntersectsRect: function (start, end, rect) {
        // Expand rectangle slightly for padding
        const padding = 10;
        const expandedRect = {
            x: rect.x - padding,
            y: rect.y - padding,
            width: rect.width + (padding * 2),
            height: rect.height + (padding * 2)
        };

        return this.lineIntersectsRectangle(start.x, start.y, end.x, end.y, expandedRect);
    },

    // Line-rectangle intersection algorithm
    lineIntersectsRectangle: function (x1, y1, x2, y2, rect) {
        const minX = Math.min(x1, x2);
        const maxX = Math.max(x1, x2);
        const minY = Math.min(y1, y2);
        const maxY = Math.max(y1, y2);

        if (maxX < rect.x || minX > rect.x + rect.width ||
            maxY < rect.y || minY > rect.y + rect.height) {
            return false;
        }

        return true;
    },

    // Update specific relationship line position
    updateRelationshipLinePosition: function (relationshipId) {
        const line = document.querySelector(`[data-relationship-id="${relationshipId}"]`);
        if (!line) return;

        // Get source and target connectors
        const sourceTableId = line.getAttribute('data-source-table-id');
        const targetTableId = line.getAttribute('data-target-table-id');
        const sourceColumnId = line.getAttribute('data-source-column-id');
        const targetColumnId = line.getAttribute('data-target-column-id');

        const sourceConnector = document.querySelector(`[data-table-id="${sourceTableId}"][data-column-id="${sourceColumnId}"]`);
        const targetConnector = document.querySelector(`[data-table-id="${targetTableId}"][data-column-id="${targetColumnId}"]`);

        if (sourceConnector && targetConnector) {
            const path = this.calculateConnectionPath(sourceConnector, targetConnector);

            if (path.length === 2) {
                // Direct connection
                line.setAttribute('x1', path[0].x);
                line.setAttribute('y1', path[0].y);
                line.setAttribute('x2', path[1].x);
                line.setAttribute('y2', path[1].y);
            } else {
                // Multi-segment connection - convert to polyline or path
                this.createMultiSegmentLine(line, path);
            }
        }
    },

    // Create multi-segment connection line
    createMultiSegmentLine: function (line, waypoints) {
        // Convert line to polyline for multi-segment paths
        if (waypoints.length > 2) {
            const points = waypoints.map(p => `${p.x},${p.y}`).join(' ');

            // Create polyline element to replace line
            const polyline = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
            polyline.setAttribute('points', points);
            polyline.setAttribute('stroke', line.getAttribute('stroke'));
            polyline.setAttribute('stroke-width', line.getAttribute('stroke-width'));
            polyline.setAttribute('stroke-dasharray', line.getAttribute('stroke-dasharray'));
            polyline.setAttribute('fill', 'none');
            polyline.setAttribute('class', line.getAttribute('class'));
            polyline.setAttribute('data-relationship-id', line.getAttribute('data-relationship-id'));

            line.parentNode.replaceChild(polyline, line);
        }
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
    },

     //Update table position programmatically (called from C#)
    updateTablePosition: function (tableId, newX, newY) {
        const tableElement = document.querySelector(`[data-table-id="${tableId}"]`);
        if (tableElement) {
            // Update visual position
            tableElement.style.transform = `translate(${newX}px, ${newY}px)`;
            tableElement.setAttribute('data-x', newX);
            tableElement.setAttribute('data-y', newY);

            // Update relationship lines
            this.updateRelationshipLines();
        }
    },

    //Animate table movement to domain
    animateTableToDomain: function (tableId, newX, newY, duration = 800) {
        const tableElement = document.querySelector(`[data-table-id="${tableId}"]`);
        if (!tableElement) return;

        const currentX = parseFloat(tableElement.getAttribute('data-x')) || 0;
        const currentY = parseFloat(tableElement.getAttribute('data-y')) || 0;

        // Add animation class
        tableElement.classList.add('moving-to-domain');

        // Animate position change
        const startTime = Date.now();
        const animateStep = () => {
            const elapsed = Date.now() - startTime;
            const progress = Math.min(elapsed / duration, 1);

            // Easing function (ease-out)
            const easeOut = 1 - Math.pow(1 - progress, 3);

            const currentAnimX = currentX + (newX - currentX) * easeOut;
            const currentAnimY = currentY + (newY - currentY) * easeOut;

            tableElement.style.transform = `translate(${currentAnimX}px, ${currentAnimY}px)`;
            tableElement.setAttribute('data-x', currentAnimX);
            tableElement.setAttribute('data-y', currentAnimY);

            // Update relationship lines during animation
            this.updateRelationshipLines();

            if (progress < 1) {
                requestAnimationFrame(animateStep);
            } else {
                // Animation complete
                tableElement.classList.remove('moving-to-domain');
                tableElement.style.transform = `translate(${newX}px, ${newY}px)`;
                tableElement.setAttribute('data-x', newX);
                tableElement.setAttribute('data-y', newY);
                this.updateRelationshipLines();
            }
        };

        requestAnimationFrame(animateStep);
    },

    //Update domain visual bounds
    updateDomainBounds: function (domainId, x, y, width, height) {
        const domainElement = document.querySelector(`[data-domain-id="${domainId}"]`);
        if (domainElement) {
            domainElement.style.left = `${x}px`;
            domainElement.style.top = `${y}px`;
            domainElement.style.width = `${width}px`;
            domainElement.style.height = `${height}px`;
        }
    },

    //Check if table is within domain bounds
    isTableInDomainBounds: function (tableElement, domainElement) {
        const tableRect = tableElement.getBoundingClientRect();
        const domainRect = domainElement.getBoundingClientRect();

        return (
            tableRect.left >= domainRect.left &&
            tableRect.top >= domainRect.top + 40 && // Account for domain header
            tableRect.right <= domainRect.right &&
            tableRect.bottom <= domainRect.bottom
        );
    },

    //Constrain table movement within domain
    constrainTableToDomain: function (tableElement, newX, newY) {
        const tableId = tableElement.getAttribute('data-table-id');

        // Find which domain this table belongs to
        if (this.dotNetHelper) {
            return this.dotNetHelper.invokeMethodAsync('GetTableDomainConstraints', tableId, newX, newY);
        }

        return { x: newX, y: newY };
    },

    // Highlight domain when selected
    highlightDomain: function (domainId) {
        // Remove previous highlights
        document.querySelectorAll('.domain-container').forEach(domain => {
            domain.classList.remove('selected');
        });

        // Add highlight to selected domain
        const domainElement = document.querySelector(`[data-domain-id="${domainId}"]`);
        if (domainElement) {
            domainElement.classList.add('selected');

            // Scroll into view if needed
            domainElement.scrollIntoView({
                behavior: 'smooth',
                block: 'center'
            });
        }
    },

    // Update domain color in real-time
    updateDomainColor: function (domainId, newColor) {
        const domainElement = document.querySelector(`[data-domain-id="${domainId}"]`);
        if (domainElement) {
            domainElement.style.backgroundColor = newColor;

            // Add a subtle animation to show the change
            domainElement.classList.add('color-updating');
            setTimeout(() => {
                domainElement.classList.remove('color-updating');
            }, 600);
        }
    },

    // Enhanced domain bounds updating with animation
    updateDomainBoundsAnimated: function (domainId, x, y, width, height, animate = true) {
        debugger;
        const domainElement = document.querySelector(`[data-domain-id="${domainId}"]`);
        if (domainElement) {
            if (animate) {
                domainElement.classList.add('auto-resizing');

                setTimeout(() => {
                    domainElement.style.left = `${x}px`;
                    domainElement.style.top = `${y}px`;
                    domainElement.style.width = `${width}px`;
                    domainElement.style.height = `${height}px`;

                    setTimeout(() => {
                        domainElement.classList.remove('auto-resizing');
                    }, 600);
                }, 50);
            } else {
                domainElement.style.left = `${x}px`;
                domainElement.style.top = `${y}px`;
                domainElement.style.width = `${width}px`;
                domainElement.style.height = `${height}px`;
            }
        }
    },

    // Show domain bounds during table drag
    showDomainBounds: function (domainId) {
        const domainElement = document.querySelector(`[data-domain-id="${domainId}"]`);
        if (domainElement) {
            const boundsHighlight = document.createElement('div');
            boundsHighlight.className = 'domain-bounds-highlight';
            boundsHighlight.id = `bounds-${domainId}`;

            const rect = domainElement.getBoundingClientRect();
            const canvasRect = this.canvasElement.getBoundingClientRect();

            boundsHighlight.style.left = `${rect.left - canvasRect.left}px`;
            boundsHighlight.style.top = `${rect.top - canvasRect.top}px`;
            boundsHighlight.style.width = `${rect.width}px`;
            boundsHighlight.style.height = `${rect.height}px`;

            this.canvasElement.appendChild(boundsHighlight);

            // Auto-remove after 2 seconds
            setTimeout(() => {
                const highlight = document.getElementById(`bounds-${domainId}`);
                if (highlight) {
                    highlight.remove();
                }
            }, 2000);
        }
    },

    // Hide domain bounds
    hideDomainBounds: function (domainId) {
        const highlight = document.getElementById(`bounds-${domainId}`);
        if (highlight) {
            highlight.remove();
        }
    },

    // Show feedback message
    showDomainFeedback: function (message, type = 'success') {
        const feedback = document.createElement('div');
        feedback.className = `domain-feedback ${type}`;
        feedback.textContent = message;

        document.body.appendChild(feedback);

        // Auto-remove after 3 seconds
        setTimeout(() => {
            feedback.style.opacity = '0';
            setTimeout(() => {
                if (feedback.parentNode) {
                    feedback.parentNode.removeChild(feedback);
                }
            }, 300);
        }, 3000);
    },

    // Enhanced table drag with domain awareness
    tableDragStartWithDomainAwareness: function (event) {
        this.tableInteractionState = 'dragging';

        const tableElement = event.target.closest('.table-card');
        const tableId = tableElement.getAttribute('data-table-id');

        // Check if table belongs to a domain
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('GetTableDomain', tableId)
                .then(domainId => {
                    if (domainId) {
                        this.showDomainBounds(domainId);
                    }
                });
        }

        console.log('Table drag started with domain awareness');
    },

    // Enhanced drag end with domain feedback
    dragEndListenerWithDomainFeedback: function (event) {
        const target = event.target;
        const tableId = target.getAttribute('data-table-id');

        this.tableInteractionState = null;

        // Hide any domain bounds highlights
        document.querySelectorAll('.domain-bounds-highlight').forEach(highlight => {
            highlight.remove();
        });

        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('UpdateTablePosition',
                tableId,
                parseFloat(target.getAttribute('data-x')) || 0,
                parseFloat(target.getAttribute('data-y')) || 0
            );
        }

        console.log('Table drag ended with domain feedback');
    },

    // Get domain constraints for table movement
    getDomainConstraints: function (tableId) {
        if (this.dotNetHelper) {
            return this.dotNetHelper.invokeMethodAsync('GetDomainConstraints', tableId);
        }
        return null;
    },

    // NEW: Check for domain collision during drag
    checkDomainCollision: function (draggedElement, newX, newY) {
        const draggedRect = {
            x: newX,
            y: newY,
            width: draggedElement.offsetWidth,
            height: draggedElement.offsetHeight
        };

        const domains = document.querySelectorAll('.domain-container');

        for (let domain of domains) {
            if (domain === draggedElement) continue;

            const domainRect = {
                x: parseFloat(domain.style.left) || 0,
                y: parseFloat(domain.style.top) || 0,
                width: domain.offsetWidth,
                height: domain.offsetHeight
            };

            if (this.domainsOverlap(draggedRect, domainRect)) {
                return true;
            }
        }

        return false;
    },

    // NEW: Check if two domain rectangles overlap
    domainsOverlap: function (rect1, rect2) {
        const buffer = 30; // Minimum spacing between domains

        return !(rect1.x + rect1.width + buffer < rect2.x ||
            rect2.x + rect2.width + buffer < rect1.x ||
            rect1.y + rect1.height + buffer < rect2.y ||
            rect2.y + rect2.height + buffer < rect1.y);
    },

    // NEW: Enhanced domain drag listener with collision detection
    domainDragMoveListener: function (event) {
        const target = event.target.closest('.domain-container');
        if (!target) return;

        let x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx;
        let y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy;

        // Check for collision with other domains
        if (this.checkDomainCollision(target, x, y)) {
            // Find nearest non-colliding position
            const adjustedPos = this.findNearestNonCollidingDomainPosition(target, x, y);
            x = adjustedPos.x;
            y = adjustedPos.y;

            // Visual feedback for collision
            target.classList.add('collision-detected');
            setTimeout(() => {
                target.classList.remove('collision-detected');
            }, 300);
        }

        // Update position
        target.style.left = `${x}px`;
        target.style.top = `${y}px`;
        target.setAttribute('data-x', x);
        target.setAttribute('data-y', y);

        // Update relationship lines
        this.updateRelationshipLines();
    },

    // NEW: Find nearest non-colliding position for domain
    findNearestNonCollidingDomainPosition: function (element, targetX, targetY) {
        const step = 20;
        const maxAttempts = 25;

        for (let attempt = 0; attempt < maxAttempts; attempt++) {
            const radius = attempt * step;

            // Try positions in a spiral pattern
            for (let angle = 0; angle < 360; angle += 45) {
                const radians = angle * Math.PI / 180;
                const testX = targetX + Math.cos(radians) * radius;
                const testY = targetY + Math.sin(radians) * radius;

                if (!this.checkDomainCollision(element, testX, testY)) {
                    return { x: testX, y: testY };
                }
            }
        }

        // If no position found, return original with offset
        return { x: targetX + 50, y: targetY + 50 };
    },

    // NEW: Setup domain interactions with collision detection
    setupDomainInteractions: function () {
        interact('.domain-container')
            .draggable({
                allowFrom: '.domain-header',
                listeners: {
                    start: this.domainDragStart.bind(this),
                    move: this.domainDragMoveListener.bind(this),
                    end: this.domainDragEnd.bind(this)
                },
                modifiers: [
                    interact.modifiers.restrictRect({
                        restriction: 'parent',
                        endOnly: true
                    })
                ]
            })
            .resizable({
                edges: { left: true, right: true, bottom: true, top: true },
                listeners: {
                    start: this.domainResizeStart.bind(this),
                    move: this.domainResizeMove.bind(this),
                    end: this.domainResizeEnd.bind(this)
                },
                modifiers: [
                    interact.modifiers.restrictSize({
                        min: { width: 300, height: 200 }
                    })
                ]
            });
    },

    // NEW: Domain drag start
    domainDragStart: function (event) {
        const target = event.target.closest('.domain-container');
        if (target) {
            target.classList.add('dragging');
            console.log('Domain drag started:', target.getAttribute('data-domain-id'));
        }
    },

    // NEW: Domain drag end with collision resolution
    domainDragEnd: function (event) {
        const target = event.target.closest('.domain-container');
        if (!target) return;

        target.classList.remove('dragging');

        const domainId = target.getAttribute('data-domain-id');
        const x = parseFloat(target.style.left) || 0;
        const y = parseFloat(target.style.top) || 0;

        // Update domain position through C# with collision detection
        if (this.dotNetHelper && domainId) {
            this.dotNetHelper.invokeMethodAsync('UpdateDomainPosition', domainId, x, y)
                .then(result => {
                    if (result.adjusted) {
                        // Position was adjusted for collision
                        target.style.left = `${result.x}px`;
                        target.style.top = `${result.y}px`;
                        this.showDomainFeedback(result.message, 'success');
                    }
                });
        }

        console.log('Domain drag ended:', domainId);
    },

    // NEW: Domain resize start
    domainResizeStart: function (event) {
        const target = event.target.closest('.domain-container');
        if (target) {
            target.classList.add('resizing');
        }
    },

    // NEW: Domain resize move with collision check
    domainResizeMove: function (event) {
        const target = event.target;
        let { x, y } = target.dataset;

        x = (parseFloat(x) || 0) + event.deltaRect.left;
        y = (parseFloat(y) || 0) + event.deltaRect.top;

        // Check if resize would cause collision
        const wouldCollide = this.checkDomainCollision(target, x, y);

        if (!wouldCollide) {
            Object.assign(target.style, {
                width: `${event.rect.width}px`,
                height: `${event.rect.height}px`,
                left: `${x}px`,
                top: `${y}px`
            });

            Object.assign(target.dataset, { x, y });
        } else {
            // Prevent resize if it would cause collision
            event.preventDefault();
            this.showDomainFeedback('Resize blocked to prevent domain overlap', 'warning');
        }

        this.updateRelationshipLines();
    },

    // NEW: Domain resize end
    domainResizeEnd: function (event) {
        const target = event.target.closest('.domain-container');
        if (target) {
            target.classList.remove('resizing');

            // Update domain size in C#
            const domainId = target.getAttribute('data-domain-id');
            if (this.dotNetHelper && domainId) {
                const rect = target.getBoundingClientRect();
                const canvasRect = this.canvasElement.getBoundingClientRect();

                this.dotNetHelper.invokeMethodAsync('UpdateDomainSize',
                    domainId, rect.width, rect.height);
            }
        }
    },

    //setupInteractions: function () {
    //    // Make tables draggable - but prevent dragging when starting from column connectors
    //    interact('.table-card')
    //        .draggable({
    //            inertia: true,
    //            // Prevent dragging when starting from column connectors or other specific elements
    //            ignoreFrom: '.column-connector, .btn, .form-check-input, .column-row',
    //            // Only allow dragging from the header area
    //            allowFrom: '.table-card-header',
    //            modifiers: [
    //                interact.modifiers.restrictRect({
    //                    restriction: 'parent',
    //                    endOnly: true
    //                })
    //            ],
    //            autoScroll: true,
    //            listeners: {
    //                start: this.tableDragStart.bind(this),
    //                move: this.dragMoveListener.bind(this),
    //                end: this.dragEndListener.bind(this)
    //            }
    //        })
    //        .resizable({
    //            edges: { left: true, right: true, bottom: true, top: true },
    //            // Prevent resizing when interacting with column connectors
    //            ignoreFrom: '.column-connector',
    //            listeners: {
    //                start: this.tableResizeStart.bind(this),
    //                move: this.resizeMoveListener.bind(this),
    //                end: this.tableResizeEnd.bind(this)
    //            },
    //            modifiers: [
    //                interact.modifiers.restrictEdges({
    //                    outer: 'parent'
    //                }),
    //                interact.modifiers.restrictSize({
    //                    min: { width: 200, height: 150 }
    //                })
    //            ],
    //            inertia: true
    //        });

    //    // Setup domain interactions
    //    interact('.domain-container')
    //        .draggable({
    //            allowFrom: '.domain-header',
    //            listeners: {
    //                move: this.dragMoveListener.bind(this)
    //            }
    //        })
    //        .resizable({
    //            edges: { left: true, right: true, bottom: true, top: true },
    //            listeners: {
    //                move: this.resizeMoveListener.bind(this)
    //            }
    //        });
    //},

    // Update the main setupInteractions to include domain interactions
    setupInteractions: function () {
        // Existing table interactions...
        this.setupTableInteractions();

        // NEW: Setup domain interactions
        this.setupDomainInteractions();
    },

    // Separate table interactions for clarity
    setupTableInteractions: function () {
        interact('.table-card')
            .draggable({
                inertia: true,
                ignoreFrom: '.column-connector, .btn, .form-check-input, .column-row',
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
    },

    // Enhanced auto-arrange with domain collision awareness
    autoArrangeDomainsWithoutCollision: function () {
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('AutoArrangeDomains');
        }
    },

    updateTableVisibilityForDomain: function (domainId, isVisible) {
        const tables = document.querySelectorAll(`[data-domain-id="${domainId}"]`);
        const animationDuration = 300;

        tables.forEach(table => {
            if (table.classList.contains('table-card')) {
                if (isVisible) {
                    // Show tables with animation
                    this.showTableWithAnimation(table, animationDuration);
                } else {
                    // Hide tables with animation
                    this.hideTableWithAnimation(table, animationDuration);
                }
            }
        });

        // Update relationship lines after visibility change
        setTimeout(() => {
            this.updateRelationshipLines();
        }, animationDuration);
    },

    // NEW: Show table with smooth animation
    showTableWithAnimation: function (tableElement, duration = 300) {
        if (!tableElement) return;

        // Remove hidden state
        tableElement.classList.remove('hidden-by-domain');

        // Start with collapsed state
        tableElement.style.transform = 'scale(0.8)';
        tableElement.style.opacity = '0';
        tableElement.style.display = 'block';
        tableElement.style.transition = `all ${duration}ms cubic-bezier(0.34, 1.56, 0.64, 1)`;

        // Animate to full visibility
        requestAnimationFrame(() => {
            tableElement.style.transform = 'scale(1)';
            tableElement.style.opacity = '1';
        });

        // Clean up transition after animation
        setTimeout(() => {
            tableElement.style.transition = '';
        }, duration);
    },

    // NEW: Hide table with smooth animation
    hideTableWithAnimation: function (tableElement, duration = 300) {
        if (!tableElement) return;

        tableElement.style.transition = `all ${duration}ms cubic-bezier(0.25, 0.46, 0.45, 0.94)`;

        // Animate to hidden state
        tableElement.style.transform = 'scale(0.8)';
        tableElement.style.opacity = '0';

        // Hide completely after animation
        setTimeout(() => {
            tableElement.style.display = 'none';
            tableElement.classList.add('hidden-by-domain');
            tableElement.style.transition = '';
        }, duration);
    },

    // NEW: Enhanced domain collapse/expand with table management
    toggleDomainCollapse: function (domainElement) {
        const domainId = domainElement.getAttribute('data-domain-id');
        const isCurrentlyCollapsed = domainElement.classList.contains('collapsed');

        // Toggle domain visual state
        if (isCurrentlyCollapsed) {
            this.expandDomain(domainElement, domainId);
        } else {
            this.collapseDomain(domainElement, domainId);
        }

        // Update C# state
        if (this.dotNetHelper) {
            this.dotNetHelper.invokeMethodAsync('SetDomainCollapsed', domainId, !isCurrentlyCollapsed);
        }
    },

    // NEW: Expand domain animation
    expandDomain: function (domainElement, domainId) {
        domainElement.classList.remove('collapsed');

        // Get original height from data attribute or calculate
        const originalHeight = domainElement.getAttribute('data-original-height') || '300';

        // Animate domain expansion
        domainElement.style.transition = 'height 0.4s cubic-bezier(0.4, 0, 0.2, 1)';
        domainElement.style.height = `${originalHeight}px`;

        // Show tables with delay for smoother effect
        setTimeout(() => {
            this.updateTableVisibilityForDomain(domainId, true);
        }, 200);

        // Clean up transition
        setTimeout(() => {
            domainElement.style.transition = '';
        }, 400);
    },

    // NEW: Collapse domain animation
    collapseDomain: function (domainElement, domainId) {
        // Store original height
        domainElement.setAttribute('data-original-height', domainElement.offsetHeight);

        // Hide tables first
        this.updateTableVisibilityForDomain(domainId, false);

        // Animate domain collapse after table hiding
        setTimeout(() => {
            domainElement.classList.add('collapsed');
            domainElement.style.transition = 'height 0.4s cubic-bezier(0.4, 0, 0.2, 1)';
            domainElement.style.height = '50px'; // Collapsed height (header only)

            // Clean up transition
            setTimeout(() => {
                domainElement.style.transition = '';
            }, 400);
        }, 100);
    },

    // NEW: Update relationship lines considering table visibility
    updateRelationshipLinesWithVisibility: function () {
        const lines = document.querySelectorAll('.relationship-line');

        lines.forEach(line => {
            const sourceTableId = line.getAttribute('data-source-table-id');
            const targetTableId = line.getAttribute('data-target-table-id');

            const sourceTable = document.querySelector(`[data-table-id="${sourceTableId}"]`);
            const targetTable = document.querySelector(`[data-table-id="${targetTableId}"]`);

            // Hide relationship line if either table is hidden
            const shouldHideLine =
                (sourceTable && sourceTable.classList.contains('hidden-by-domain')) ||
                (targetTable && targetTable.classList.contains('hidden-by-domain'));

            if (shouldHideLine) {
                line.style.display = 'none';
                line.style.opacity = '0';
            } else {
                line.style.display = 'block';
                line.style.opacity = '1';

                // Update line position if both tables are visible
                this.updateRelationshipLinePosition(line.getAttribute('data-relationship-id'));
            }
        });
    },

    updateRelationshipLines: function () {
        this.updateRelationshipLinesWithVisibility();
    },

    // NEW: Get visible tables count for domain
    getVisibleTablesInDomain: function (domainId) {
        const tables = document.querySelectorAll(`[data-domain-id="${domainId}"].table-card`);
        let visibleCount = 0;

        tables.forEach(table => {
            if (!table.classList.contains('hidden-by-domain') &&
                table.style.display !== 'none') {
                visibleCount++;
            }
        });

        return visibleCount;
    },

    // NEW: Update domain header with table count
    updateDomainTableCount: function (domainId) {
        const domainElement = document.querySelector(`[data-domain-id="${domainId}"]`);
        if (!domainElement) return;

        const countElement = domainElement.querySelector('.domain-table-count');
        if (countElement) {
            const visibleCount = this.getVisibleTablesInDomain(domainId);
            const totalCount = document.querySelectorAll(`[data-domain-id="${domainId}"].table-card`).length;
            const isCollapsed = domainElement.classList.contains('collapsed');

            countElement.textContent = isCollapsed
                ? `${totalCount} tables (hidden)`
                : `${visibleCount}/${totalCount} tables`;
        }
    },

    // NEW: Handle domain header click for expand/collapse
    setupDomainToggleHandlers: function () {
        document.addEventListener('click', (e) => {
            const toggleBtn = e.target.closest('.domain-toggle-btn');
            if (toggleBtn) {
                e.preventDefault();
                e.stopPropagation();

                const domainElement = toggleBtn.closest('.domain-container');
                if (domainElement) {
                    this.toggleDomainCollapse(domainElement);
                }
            }
        });
    },

    // NEW: Initialize domain collapse functionality
    initializeDomainCollapse: function () {
        this.setupDomainToggleHandlers();

        // Set up observer for dynamic domain updates
        if (window.MutationObserver) {
            const observer = new MutationObserver((mutations) => {
                mutations.forEach((mutation) => {
                    if (mutation.type === 'childList') {
                        // Update counts when domains are added/removed
                        const addedDomains = Array.from(mutation.addedNodes)
                            .filter(node => node.classList && node.classList.contains('domain-container'));

                        addedDomains.forEach(domain => {
                            const domainId = domain.getAttribute('data-domain-id');
                            if (domainId) {
                                this.updateDomainTableCount(domainId);
                            }
                        });
                    }
                });
            });

            observer.observe(this.canvasElement, {
                childList: true,
                subtree: true
            });
        }
    },

    downloadFile: function (filename, content, contentType) {
        const blob = new Blob([content], { type: contentType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    openResultsWindow: function (htmlContent) {
        const newWindow = window.open('', '_blank', 'width=1000,height=600,scrollbars=yes,resizable=yes');
        newWindow.document.write(htmlContent);
        newWindow.document.close();
    },
};

const style = document.createElement('style');
style.textContent = `
    .color-updating {
        animation: color-pulse 0.6s ease-in-out;
    }
    
    @keyframes color-pulse {
        0%, 100% { transform: scale(1); }
        50% { transform: scale(1.02); filter: brightness(1.1); }
    }
`;
document.head.appendChild(style);


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

export function refreshAllRelationshipLines() {
    window.sqlBuilderInterop.refreshAllRelationshipLines();
}

export function autoArrangeTables() {
    window.sqlBuilderInterop.autoArrangeTables();
}

export function updateTablePosition(tableId, x, y) {
    window.sqlBuilderInterop.updateTablePosition(tableId, x, y);
}

export function animateTableToDomain(tableId, x, y, duration) {
    window.sqlBuilderInterop.animateTableToDomain(tableId, x, y, duration);
}

export function updateDomainBounds(domainId, x, y, width, height) {
    window.sqlBuilderInterop.updateDomainBounds(domainId, x, y, width, height);
}
export function highlightDomain(domainId) {
    window.sqlBuilderInterop.highlightDomain(domainId);
}

export function updateDomainColor(domainId, color) {
    window.sqlBuilderInterop.updateDomainColor(domainId, color);
}

export function showDomainFeedback(message, type) {
    window.sqlBuilderInterop.showDomainFeedback(message, type);
}

export function autoArrangeDomains() {
    window.sqlBuilderInterop.autoArrangeDomainsWithoutCollision();
}

export function updateTableVisibilityForDomain(domainId, isVisible) {
    window.sqlBuilderInterop.updateTableVisibilityForDomain(domainId, isVisible);
}

export function toggleDomainCollapse(domainElement) {
    window.sqlBuilderInterop.toggleDomainCollapse(domainElement);
}

export function updateDomainBoundsAnimated(domainId, x, y, width, height, animate = true) {
    window.sqlBuilderInterop.updateDomainBoundsAnimated(domainId, x, y, width, height, animate = true);
}

export function downloadFile(filename, content, contentType) {
    window.sqlBuilderInterop.downloadFile(filename, content, contentType);
}

export function openResultsWindow(htmlContent) {
    window.sqlBuilderInterop.openResultsWindow(htmlContent);
}

// Make functions available globally
window.initializeSqlCanvas = initializeSqlCanvas;
window.setCanvasZoom = setCanvasZoom;
window.showModal = showModal;
window.hideModal = hideModal;
window.saveToLocalStorage = saveToLocalStorage;
window.loadFromLocalStorage = loadFromLocalStorage;
window.autoArrangeTables = autoArrangeTables;
window.refreshAllRelationshipLines = refreshAllRelationshipLines;
window.updateTablePosition = updateTablePosition;
window.animateTableToDomain = animateTableToDomain;
window.updateDomainBounds = updateDomainBounds;
window.highlightDomain = highlightDomain;
window.updateDomainColor = updateDomainColor;
window.showDomainFeedback = showDomainFeedback;
window.autoArrangeDomains = autoArrangeDomains;
window.updateTableVisibilityForDomain = updateTableVisibilityForDomain;
window.toggleDomainCollapse = toggleDomainCollapse;
window.updateDomainBoundsAnimated = updateDomainBoundsAnimated;
window.downloadFile = downloadFile;
window.openResultsWindow = openResultsWindow;
