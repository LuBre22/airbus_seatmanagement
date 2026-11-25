Feature: Flight dispatch workflows
  Integration workflows preserve imported passenger data, cabin state, and audit history.

  Scenario: Import keeps the first duplicate and reports invalid manifest data
    Given the sample manifest has been imported
    Then the first record for "K7M4QZ" is accepted
    And invalid date and miles fields are reported

  Scenario: Imported preassignments and new assignments remain consistent
    Given the sample manifest has been imported
    When I load imported passengers and apply their preassignments
    Then the flight state is consistent

  Scenario: Audit hashes are independent of current culture
    When I create audit hashes under German, English, and Thai cultures
    Then all generated audit hashes are identical

  Scenario: Blocking and assigning seats updates state and audit chain
    When I block a seat and assign a passenger
    Then the flight state and audit chain are valid

  Scenario: A failed batch rolls back state and records its audit events
    When I execute a batch that blocks the same seat twice
    Then the state is restored and the failed batch is audited

  Scenario: A frozen dispatch requires supervisor authorization
    When I attempt to block a seat before and after supervisor authorization
    Then only the supervisor operation succeeds and the audit chain is valid

  Scenario: The standard layout exposes its expected seat counts
    Then the standard cabin layout contains the characterized seats

  Scenario: Manifest export is deterministic and has no byte order mark
    When I export a passenger manifest
    Then the export is deterministic and contains no byte order mark

  Scenario: Atomic CSV writes replace the destination with complete content
    When I atomically write a CSV file
    Then the destination contains the complete replacement content
