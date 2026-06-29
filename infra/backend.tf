terraform {
  backend "s3" {
    bucket       = "andre-lopes-iac"
    key          = "bolaomaisberlim.tfstate"
    encrypt      = true
    use_lockfile = true
    region       = "eu-central-1"
  }
}
