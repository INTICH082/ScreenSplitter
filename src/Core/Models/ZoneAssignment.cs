namespace ScreenSplitter.Core.Models;

public enum ZoneAssignmentKind { Empty, Free, App }

public record ZoneAssignment(int Col, int Row, ZoneAssignmentKind Kind, string? Target, string? DisplayName);