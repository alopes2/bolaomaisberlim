locals {
  dynamodb_tables = {
    participants = {
      name       = "${local.name_prefix}-participants"
      hash_key   = "ParticipantId"
      range_key  = null
      attributes = ["ParticipantId"]
    }
    matches = {
      name       = "${local.name_prefix}-matches"
      hash_key   = "MatchId"
      range_key  = null
      attributes = ["MatchId"]
    }
    predictions = {
      name       = "${local.name_prefix}-predictions"
      hash_key   = "MatchId"
      range_key  = "ParticipantId"
      attributes = ["MatchId", "ParticipantId"]
    }
    standings = {
      name       = "${local.name_prefix}-standings"
      hash_key   = "ParticipantId"
      range_key  = null
      attributes = ["ParticipantId"]
    }
  }
}

resource "aws_dynamodb_table" "this" {
  for_each = local.dynamodb_tables

  name         = each.value.name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = each.value.hash_key
  range_key    = each.value.range_key

  dynamic "attribute" {
    for_each = toset(each.value.attributes)
    content {
      name = attribute.value
      type = "S"
    }
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }
}
