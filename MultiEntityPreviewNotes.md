# Multi-Entity Preview Feature Implementation

## Overview
This document outlines the implementation plan for enhancing the multi-entity preview functionality in the BlackoutScanner application. The goal is to improve the user experience when working with multi-row data capture (like leaderboards) by immediately showing ghost outlines for all fields across multiple rows.

## Current Status
The basic implementation has been started but has compilation issues due to XAML code-behind references. The temporary solution is to continue using the `MultiEntityPreviewHelper` class until the enhanced version can be fully implemented.

## Implementation Plan

### 1. AreaSelectorWindow Enhancements
- Added properties to support multi-entity preview mode:
  - `IsMultiEntityPreviewMode`: Flag to indicate we're in preview mode
  - `CategoryToPreview`: The category containing fields to preview
  - `UpdatedHeightOffset`: The spacing between rows that the user can adjust
  
- Key methods to implement:
  - `SetupMultiEntityPreview()`: Sets up the UI for multi-entity preview
  - `DrawAllMultiEntityFields()`: Draws ghost outlines for all fields across multiple rows
  
- Enhanced event handlers:
  - `Window_PreviewKeyDown`: For keyboard shortcuts to adjust row spacing
  - `Canvas_MouseDown`: To initiate vertical-only dragging for row spacing adjustment
  - `Canvas_MouseMove`: To handle vertical-only dragging for spacing adjustment
  - `Canvas_MouseUp`: To finalize the row spacing
  - `ConfirmSelection`: To return the updated height offset
  
### 2. ProfileEditorWindow Integration
- Modify `OnPreviewMultiEntity` method to use the new AreaSelectorWindow implementation
- Return and apply the updated row height offset to the category

### 3. Required XAML Fixes
The current implementation has compile errors because:
1. XAML elements like `selectionCanvas`, `selectionRectangle`, etc. are not properly referenced
2. The XAML file doesn't match the expected elements in the code-behind

### Temporary Solution
Continue using the existing `MultiEntityPreviewHelper` class until the enhanced version can be fully implemented.

## Next Steps
1. Fix the XAML-related compilation issues
2. Test the multi-entity preview functionality
3. Enhance the UI with clearer visual indicators and feedback
4. Update documentation

## Expected User Experience
When the user clicks the preview button for multi-entity mode:
1. The AreaSelectorWindow opens showing all defined fields highlighted
2. The fields are repeated for each row based on the current height offset
3. The user can drag up/down to adjust row spacing or use keyboard shortcuts
4. Visual feedback shows the current spacing value
5. Pressing ENTER confirms the spacing, pressing ESC cancels

This approach will provide a much more intuitive and visual way to adjust multi-entity capture settings.
