require 'bundler/setup'

require 'albacore'
require 'albacore/tasks/versionizer'
require 'albacore/ext/teamcity'

Albacore::Tasks::Versionizer.new :versioning

nugets_restore :restore do |p|
  p.out = 'packages'
  p.exe = 'buildsupport/NuGet.exe'
end

desc "Perform full build"
build :build => [:versioning, :restore] do |b|
  b.sln = 'FSharp.EventSource.sln'
end

directory 'build/pkg'

desc "package nugets"
nugets_pack :create_nugets => ['build/pkg', :versioning, :build] do |p|
  p.files   = FileList['**/*.{fsproj,nuspec}'].
    exclude(/Tests/).
    exclude(/Spec/).
    exclude(/sample/).
    exclude(/packages/)
  p.out     = 'build/pkg'
  p.exe     = 'buildsupport/NuGet.exe'
  
  p.with_metadata do |m|
    m.version       = ENV['NUGET_VERSION']
    m.authors       = 'Henrik Feldt'
    m.description   = 'A library for sending EventSource events to your web browser.'
    m.language      = 'en-GB'
    m.copyright     = 'Henrik Feldt'
    m.release_notes = "Full version: #{ENV['BUILD_VERSION']}."
  end

  p.gen_symbols
end

task :default => :create_nugets
